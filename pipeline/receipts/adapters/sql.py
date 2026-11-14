"""SQL migration adapter (SPEC 5.3, second adapter): a throwaway Postgres in Docker per review.

Flow: start postgres:16-alpine (--rm, unique name, random 127.0.0.1 port), wait until it accepts TCP
connections, apply the base commit's migrations, snapshot, apply the head's new up migrations,
snapshot, run the head's queries/*.sql, apply the new down migrations in reverse, snapshot, and
always remove the container in `finally`. Every docker/psql call is a CommandReceipt; the SQL fed
to psql is saved under run_dir/inputs/ and named in the log, so the receipt holds its own input.

Snapshot: schema lines (columns, constraints, indexes; compared as a set, so column order is
ignored), the row count of each table, and a checksum per column (md5 of its non-null values sorted
as text, plus the non-null count). The comparisons are pure functions, tested without Docker:
  reversible  schema after down == schema at base
  rows        no table has fewer rows after up than at base
  values      for every rename the requirement asks for, the old column's checksum at base equals
              the new column's checksum after up (a drop + add loses them)
"""

import difflib
import os
import re
import shutil
import uuid
from dataclasses import dataclass, field
from pathlib import Path

from .. import sandbox
from ..gitutil import git
from ..models import CommandReceipt, Evidence, Finding, NotChecked, ReceiptPlan
from .base import AdapterInput, AdapterResult

IMAGE = "postgres:16-alpine"
CMD_TIMEOUT_S = 120
READY_TIMEOUT_S = 90
CONTAINER_PREFIX = "receipts-sql-"
UP, DOWN = ".up.sql", ".down.sql"
PSQL = ["psql", "-U", "postgres", "-d", "postgres", "-X", "-v", "ON_ERROR_STOP=1"]
READY_LOOP = ("for i in $(seq 1 120); do pg_isready -q -h 127.0.0.1 -U postgres && exit 0; "
              "sleep 0.5; done; echo 'postgres did not accept TCP connections'; exit 1")

# One psql call, tab-separated rows, each tagged with what it describes.
SNAPSHOT_SQL = r"""
SELECT 'COLUMN', table_name || '.' || column_name, data_type, is_nullable,
       coalesce(column_default, '')
FROM information_schema.columns WHERE table_schema = 'public' ORDER BY 2;
SELECT 'CONSTRAINT', table_name, constraint_name, constraint_type
FROM information_schema.table_constraints
WHERE constraint_schema = 'public' AND constraint_name NOT LIKE '%\_not\_null' ORDER BY 2, 3;
SELECT 'INDEX', tablename, indexname, indexdef
FROM pg_indexes WHERE schemaname = 'public' ORDER BY 2, 3;
SELECT 'ROWS', t.table_name, (xpath('/row/c/text()', query_to_xml(
         format('SELECT count(*) AS c FROM public.%I', t.table_name), false, true, '')))[1]::text
FROM information_schema.tables t
WHERE t.table_schema = 'public' AND t.table_type = 'BASE TABLE' ORDER BY 2;
SELECT 'VALUES', c.table_name || '.' || c.column_name, (xpath('/row/h/text()', query_to_xml(
         format('SELECT md5(coalesce(string_agg(%1$I::text, %2$L ORDER BY %1$I::text), %3$L))'
                ' || %4$L || count(%1$I) AS h FROM public.%5$I',
                c.column_name, '|', '', ':', c.table_name), false, true, '')))[1]::text
FROM information_schema.columns c
JOIN information_schema.tables t
  ON t.table_schema = c.table_schema AND t.table_name = c.table_name
WHERE c.table_schema = 'public' AND t.table_type = 'BASE TABLE' ORDER BY 2;
"""

NOT_CHECKED = [
    NotChecked(what="performance of the migration on production-size data",
               why="the sandbox seeds the repo's own fixture rows (5); duration and lock time on "
                   "large tables are unknown"),
    NotChecked(what="locking behaviour under concurrent writes",
               why="migrations ran on an idle database over a single connection"),
    NotChecked(what="values in columns the requirement does not name",
               why="only row counts and the checksums of columns the requirement asks to rename "
                   "are compared"),
    NotChecked(what="data restored by the down migration",
               why="down is checked for schema equality with base; rows and values it fails to "
                   "bring back are not compared"),
    NotChecked(what="other Postgres versions and other databases",
               why=f"the sandbox runs {IMAGE} only"),
    NotChecked(what="application code that reads these tables",
               why="only the repo's queries/*.sql were run against the migrated schema"),
]


def is_migration(path: str) -> bool:
    return path.startswith("migrations/") and path.endswith(".sql")


def area_for(path: str) -> str:
    if is_migration(path):
        return "db-migrations"
    if path.startswith("queries/"):
        return "db-queries"
    return "docs" if path.endswith(".md") else "other"


def docker_env() -> dict[str, str]:
    """Allowlist like sandbox.dotnet_env: the docker CLI needs its config, never API keys."""
    env = {k: v for k, v in os.environ.items()
           if k in sandbox.ENV_PASS or k.startswith("DOCKER_")}
    return {k: v for k, v in env.items() if not sandbox.ENV_DENY.search(k)}


# Snapshots and comparisons (pure) -----------------------------------------------------------


@dataclass
class Snapshot:
    schema: list[str] = field(default_factory=list)  # sorted
    rows: dict[str, int] = field(default_factory=dict)  # table -> count
    columns: dict[str, str] = field(default_factory=dict)  # "table.column" -> checksum


def parse_snapshot(text: str) -> Snapshot:
    snap = Snapshot()
    for line in text.splitlines():
        parts = line.split("\t")
        tag = parts[0]
        if tag == "COLUMN" and len(parts) == 5:
            snap.schema.append(f"column {parts[1]} {parts[2]} nullable={parts[3]} "
                               f"default={parts[4]}")
        elif tag == "CONSTRAINT" and len(parts) == 4:
            snap.schema.append(f"constraint {parts[1]}.{parts[2]} {parts[3]}")
        elif tag == "INDEX" and len(parts) == 4:
            snap.schema.append(f"index {parts[1]}.{parts[2]} {parts[3]}")
        elif tag == "ROWS" and len(parts) == 3:
            snap.rows[parts[1]] = int(parts[2])
        elif tag == "VALUES" and len(parts) == 3:
            snap.columns[parts[1]] = parts[2]
    snap.schema.sort()
    return snap


def schema_diff(before: Snapshot, after: Snapshot) -> str:
    return "\n".join(difflib.unified_diff(before.schema, after.schema, "schema at base",
                                          "schema after down", lineterm="", n=0))


def lost_rows(before: Snapshot, after: Snapshot) -> dict[str, tuple[int, int]]:
    """table -> (rows before, rows after) for every table that has fewer rows (or vanished)."""
    return {t: (n, after.rows.get(t, 0)) for t, n in sorted(before.rows.items())
            if after.rows.get(t, 0) < n}


_RENAME = re.compile(r"\brename\s+`(\w+)`\s+to\s+`(\w+)`", re.I)


def requested_renames(requirement_text: str) -> list[tuple[str, str]]:
    return _RENAME.findall(requirement_text)


def short(checksum: str | None) -> str:
    if not checksum:
        return "missing"
    digest, _, count = checksum.partition(":")
    return f"{digest[:12]}:{count}" if count else digest[:12]


def rename_checks(before: Snapshot, after: Snapshot,
                  renames: list[tuple[str, str]]) -> list[dict]:
    """One row per (table, rename): did the values survive under the new name?"""
    rows = []
    for old, new in renames:
        tables = sorted(k.split(".", 1)[0] for k in before.columns if k.split(".", 1)[1] == old)
        if not tables:
            rows.append({"column": f"{old} -> {new}", "base": "missing", "after_up": "-",
                         "preserved": False})
        for t in tables:
            b, a = before.columns[f"{t}.{old}"], after.columns.get(f"{t}.{new}")
            rows.append({"column": f"{t}.{old} -> {t}.{new}", "base": short(b),
                         "after_up": short(a), "preserved": a == b})
    return rows


@dataclass
class Plan:
    base_ups: list[str]  # applied from the base commit, in order
    new: list[tuple[str, str | None]]  # (up, down or None) added by the head, in order
    queries: list[str]
    edited: list[str]  # base migrations the head changed


def plan(base_files: list[str], head_files: list[str], changed: list[str]) -> Plan:
    base_set, head_set = set(base_files), set(head_files)
    new_ups = sorted(p for p in head_set - base_set if is_migration(p) and p.endswith(UP))
    return Plan(
        base_ups=sorted(p for p in base_set if is_migration(p) and p.endswith(UP)),
        new=[(u, d if (d := u[: -len(UP)] + DOWN) in head_set else None) for u in new_ups],
        queries=sorted(p for p in head_set if p.startswith("queries/") and p.endswith(".sql")),
        edited=sorted(p for p in changed if p in base_set and is_migration(p)),
    )


@dataclass
class Trace:
    """What the container run produced; evaluate() turns it into Evidence."""

    plan: Plan
    up: list[CommandReceipt] = field(default_factory=list)
    down: list[CommandReceipt] = field(default_factory=list)
    queries: list[tuple[str, CommandReceipt]] = field(default_factory=list)
    snap_ids: dict[str, str] = field(default_factory=dict)  # "base" | "up" | "down" -> command id
    snaps: dict[str, Snapshot] = field(default_factory=dict)


def _reversible(tr: Trace) -> tuple[Evidence, str]:
    ids = [c.id for c in tr.up + tr.down] + [tr.snap_ids[k] for k in ("base", "down")
                                             if k in tr.snap_ids]
    ev = dict(id="sql-reversible", kind="migration-up-down", command_ids=ids)
    new = tr.plan.new
    if not new:
        return Evidence(**ev, title="No new migration in this change", result="skipped"), ""
    failed_up = next((c for c in tr.up if c.exit_code != 0), None)
    if failed_up:
        up_path = new[len(tr.up) - 1][0]
        title = f"Up migration {up_path} failed (exit {failed_up.exit_code})"
        return Evidence(**ev, result="fail", title=title), up_path
    missing = [u for u, d in new if d is None]
    if missing:
        return Evidence(**ev, result="fail",
                        title=f"Migration not reversible: no down migration for {missing[0]}"), \
            missing[0]
    down_paths = [d for _, d in reversed(new)]
    failed_down = next((c for c in tr.down if c.exit_code != 0), None)
    if failed_down:
        path = down_paths[len(tr.down) - 1]
        return Evidence(**ev, result="fail",
                        title=f"Migration not reversible: {path} failed "
                              f"(exit {failed_down.exit_code})"), path
    base, down = tr.snaps.get("base"), tr.snaps.get("down")
    if base is None or down is None:
        return Evidence(**ev, result="error", title="Schema snapshot missing; "
                                                     "reversibility not judged"), ""
    diff = schema_diff(base, down)
    if diff:
        changed = sum(1 for ln in diff.splitlines()
                      if ln[:1] in "+-" and not ln.startswith(("+++", "---")))
        return Evidence(**ev, result="fail", details={"schema_diff": diff},
                        title=f"Migration not reversible: schema after down differs from base "
                              f"({changed} line(s))"), down_paths[0]
    return Evidence(**ev, result="pass", details={"schema_objects": len(base.schema)},
                    title=f"Down migration restores the base schema exactly "
                          f"({len(base.schema)} schema objects compared)"), ""


def _rows(tr: Trace) -> tuple[Evidence, str] | None:
    base, up = tr.snaps.get("base"), tr.snaps.get("up")
    if base is None or up is None:
        return None
    down = tr.snaps.get("down")
    tables = sorted(set(base.rows) | set(up.rows))
    table = [{"table": t, "base": base.rows.get(t, "-"), "after_up": up.rows.get(t, "-"),
              "after_down": down.rows.get(t, "-") if down else "-"} for t in tables]
    lost = lost_rows(base, up)
    ids = [tr.snap_ids["base"], tr.snap_ids["up"]] + ([tr.snap_ids["down"]] if down else [])
    ev = dict(id="sql-rows", kind="row-preservation", command_ids=ids,
              details={"row_counts": table})
    if lost:
        what = ", ".join(f"{t} {b} -> {a}" for t, (b, a) in lost.items())
        return Evidence(**ev, result="fail", title=f"Rows lost across the up migration: {what}"), \
            tr.plan.new[0][0] if tr.plan.new else ""
    what = ", ".join(f"{t} {base.rows[t]} -> {up.rows.get(t, 0)}" for t in sorted(base.rows))
    return Evidence(**ev, result="pass", title=f"No rows lost across the up migration ({what})"), ""


def _values(tr: Trace, renames: list[tuple[str, str]]) -> tuple[Evidence, str] | None:
    base, up = tr.snaps.get("base"), tr.snaps.get("up")
    if not renames or base is None or up is None:
        return None
    checks = rename_checks(base, up, renames)
    ev = dict(id="sql-values", kind="row-preservation",
              command_ids=[tr.snap_ids["base"], tr.snap_ids["up"]],
              details={"value_checksums": [{**c, "preserved": "yes" if c["preserved"] else "no"}
                                           for c in checks]})
    bad = [c["column"] for c in checks if not c["preserved"]]
    if bad:
        return Evidence(**ev, result="fail",
                        title=f"Values lost in rename: {', '.join(bad)} (checksum of the values "
                              "differs after up)"), tr.plan.new[0][0] if tr.plan.new else ""
    return Evidence(**ev, result="pass",
                    title=f"Values preserved through rename: "
                          f"{', '.join(c['column'] for c in checks)}"), ""


def _queries(tr: Trace) -> tuple[Evidence, str] | None:
    if not tr.queries:
        return None
    failed = [(p, c) for p, c in tr.queries if c.exit_code != 0]
    n = len(tr.queries)
    ev = dict(id="sql-queries", kind="existing-tests", command_ids=[c.id for _, c in tr.queries],
              details={"files": [p for p, _ in tr.queries]})
    if failed:
        return Evidence(**ev, result="fail",
                        title=f"Repo queries on the migrated schema: {n - len(failed)}/{n} "
                              f"succeeded; {failed[0][0]} failed"), failed[0][0]
    return Evidence(**ev, result="pass",
                    title=f"Repo queries on the migrated schema: {n}/{n} succeeded"), ""


def evaluate(tr: Trace, renames: list[tuple[str, str]]) -> list[tuple[Evidence, str]]:
    """(evidence, location of the defect when it fails)."""
    out = [_reversible(tr)]
    for item in (_rows(tr), _values(tr, renames), _queries(tr)):
        if item is not None:
            out.append(item)
    return out


def findings_for(results: list[tuple[Evidence, str]]) -> list[Finding]:
    """No receipt, no finding: one verified finding per failed evidence item, citing it."""
    return [Finding(id=f"F-{ev.id}", lens="compatibility", claim=ev.title, location=loc,
                    severity="high", verified=True, evidence_ids=[ev.id],
                    receipt_plan=ReceiptPlan(
                        kind="test", detail="apply the migration up and down in a throwaway "
                                            "Postgres and compare schema, row and value snapshots"))
            for ev, loc in results if ev.result == "fail"]


# Container run --------------------------------------------------------------------------------


class SandboxError(RuntimeError):
    pass


class _Runner:
    def __init__(self, inp: AdapterInput, res: AdapterResult, name: str):
        self.inp, self.res, self.name = inp, res, name
        self.inputs = inp.run_dir / "inputs"
        self.inputs.mkdir(parents=True, exist_ok=True)

    def cmd(self, rid: str, argv: list[str], sql: str | None = None,
            timeout: int = CMD_TIMEOUT_S) -> CommandReceipt:
        stdin = None
        if sql is not None:
            stdin = self.inputs / f"{rid}.sql"
            stdin.write_text(sql)
        c = sandbox.run_cmd(rid, argv, self.inp.run_dir, self.inp.run_dir, timeout,
                            env=docker_env(), stdin_path=stdin)
        self.res.commands.append(c)
        return c

    def psql(self, rid: str, sql: str, *flags: str) -> CommandReceipt:
        return self.cmd(rid, ["docker", "exec", "-i", self.name, *PSQL, *flags], sql)

    def apply(self, rid: str, sha: str, path: str) -> CommandReceipt:
        return self.psql(rid, git("show", f"{sha}:{path}", cwd=self.inp.repo), "-a")

    def snapshot(self, rid: str) -> Snapshot:
        c = self.psql(rid, SNAPSHOT_SQL, "-q", "-A", "-t", "-F", "\t")
        if c.exit_code != 0:
            raise SandboxError(f"snapshot {rid} failed (exit {c.exit_code})")
        return parse_snapshot((self.inp.run_dir / c.log_path).read_text())


def _stem(path: str) -> str:
    return Path(path).name.split(".")[0]


def _execute(r: _Runner, inp: AdapterInput, tr: Trace) -> None:
    start = r.cmd("sql-start", ["docker", "run", "-d", "--rm", "--name", r.name,
                                "-e", "POSTGRES_HOST_AUTH_METHOD=trust",
                                "-p", "127.0.0.1::5432", IMAGE])
    if start.exit_code != 0:
        raise SandboxError(f"container did not start (exit {start.exit_code})")
    ready = r.cmd("sql-ready", ["docker", "exec", r.name, "sh", "-c", READY_LOOP],
                  timeout=READY_TIMEOUT_S)
    if ready.exit_code != 0:
        raise SandboxError("postgres never became ready")
    r.cmd("sql-port", ["docker", "port", r.name, "5432/tcp"])
    for path in tr.plan.base_ups:
        c = r.apply(f"sql-base-up-{_stem(path)}", inp.base_sha, path)
        if c.exit_code != 0:
            raise SandboxError(f"base migration {path} failed (exit {c.exit_code})")
    tr.snap_ids["base"] = "sql-snapshot-base"
    tr.snaps["base"] = r.snapshot("sql-snapshot-base")
    for up, _ in tr.plan.new:
        c = r.apply(f"sql-up-{_stem(up)}", inp.head_sha, up)
        tr.up.append(c)
        if c.exit_code != 0:
            return
    tr.snap_ids["up"] = "sql-snapshot-up"
    tr.snaps["up"] = r.snapshot("sql-snapshot-up")
    for q in tr.plan.queries:
        tr.queries.append((q, r.apply(f"sql-query-{_stem(q)}", inp.head_sha, q)))
    if any(d is None for _, d in tr.plan.new):
        return
    for _, down in reversed(tr.plan.new):
        c = r.apply(f"sql-down-{_stem(down)}", inp.head_sha, down)
        tr.down.append(c)
        if c.exit_code != 0:
            return
    tr.snap_ids["down"] = "sql-snapshot-down"
    tr.snaps["down"] = r.snapshot("sql-snapshot-down")


def _files(repo: Path, sha: str) -> list[str]:
    return git("ls-tree", "-r", "--name-only", sha, cwd=repo).split()


def run(inp: AdapterInput) -> AdapterResult:
    res = AdapterResult(not_checked=list(NOT_CHECKED))
    changed = git("diff", "--name-only", inp.base_sha, inp.head_sha, cwd=inp.repo).split()
    tr = Trace(plan=plan(_files(inp.repo, inp.base_sha), _files(inp.repo, inp.head_sha), changed))
    if tr.plan.edited:
        res.not_checked.append(NotChecked(
            what=f"edits to already-applied migrations: {', '.join(tr.plan.edited)}",
            why="base migrations are applied from the base commit; databases that already ran "
                "them never see the edit"))
    if shutil.which("docker") is None:
        res.evidence.append(Evidence(id="sql-sandbox", kind="migration-up-down", result="error",
                                     title="Docker not found; the SQL sandbox did not run"))
        return res
    r = _Runner(inp, res, f"{CONTAINER_PREFIX}{uuid.uuid4().hex[:10]}")
    try:
        _execute(r, inp, tr)
    except SandboxError as e:
        res.evidence.append(Evidence(id="sql-sandbox", kind="migration-up-down", result="error",
                                     title=f"SQL sandbox error: {e}"))
    finally:
        r.cmd("sql-teardown", ["docker", "rm", "-f", r.name])
    if not any(e.id == "sql-sandbox" for e in res.evidence):
        results = evaluate(tr, requested_renames(inp.requirement_text))
        res.evidence += [ev for ev, _ in results]
        res.findings += findings_for(results)
        res.ran += ["migration-up-down", "row-preservation"]
    res.extra = {"image": IMAGE, "container": r.name,
                 "snapshots": {k: vars(v) for k, v in tr.snaps.items()},
                 "plan": vars(tr.plan)}
    return res
