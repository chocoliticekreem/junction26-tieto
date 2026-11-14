import json
import os
import subprocess

import pytest

from receipts import adapters, config
from receipts.adapters import sql
from receipts.adapters.sql import Plan, Snapshot, Trace
from receipts.models import CommandReceipt, FileChange, Hunk, IntakeReport, RoutingReport
from receipts.verdict import VerdictInputs, compose

BASE_TEXT = "\n".join([
    "$ docker exec -i c psql ...",
    "# started: now",
    "",
    "COLUMN\torders.amount\tinteger\tNO\t",
    "COLUMN\torders.id\tinteger\tNO\tnextval('orders_id_seq'::regclass)",
    "CONSTRAINT\torders\torders_pkey\tPRIMARY KEY",
    "INDEX\torders\torders_pkey\tCREATE UNIQUE INDEX orders_pkey ON public.orders (id)",
    "ROWS\torders\t5",
    "VALUES\torders.amount\taaaa:5",
    "VALUES\torders.id\tbbbb:5",
    "",
    "# exit: 0  duration: 0.07s",
])

REQ_RENAME = "Rename `amount` to `amount_cents` on `orders`. Every order keeps its amount."


def snap(columns: dict[str, str], rows: dict[str, int] | None = None,
         extra_schema: list[str] | None = None) -> Snapshot:
    schema = sorted([f"column {c} integer nullable=NO default=" for c in columns]
                    + (extra_schema or []))
    return Snapshot(schema=schema, rows=rows or {"orders": 5}, columns=dict(columns))


def receipt(cid: str, code: int = 0) -> CommandReceipt:
    return CommandReceipt(id=cid, cmd=["docker"], cwd=".", exit_code=code, duration_s=0.1,
                          log_path=f"logs/{cid}.log")


BASE = snap({"orders.amount": "aaaa:5", "orders.id": "bbbb:5"})
UP_ADD = snap({"orders.amount": "aaaa:5", "orders.id": "bbbb:5", "orders.discount_cents": "z:5"})
NEW = [("migrations/002_x.up.sql", "migrations/002_x.down.sql")]


def trace(new=NEW, up_codes=(0,), down_codes=(0,), snaps=None, queries=()) -> Trace:
    tr = Trace(plan=Plan(base_ups=["migrations/001_a.up.sql"], new=list(new),
                         queries=[q for q, _ in queries], edited=[]))
    tr.up = [receipt(f"sql-up-{i}", c) for i, c in enumerate(up_codes)]
    tr.down = [receipt(f"sql-down-{i}", c) for i, c in enumerate(down_codes)]
    tr.queries = [(q, receipt(f"sql-query-{i}", c)) for i, (q, c) in enumerate(queries)]
    tr.snaps = snaps or {}
    tr.snap_ids = {k: f"sql-snapshot-{k}" for k in tr.snaps}
    return tr


def by_id(results):
    return {ev.id: (ev, loc) for ev, loc in results}


# Snapshot parsing and pure comparisons --------------------------------------------------------


def test_parse_snapshot_reads_tagged_rows_and_ignores_log_noise():
    s = sql.parse_snapshot(BASE_TEXT)
    assert s.rows == {"orders": 5}
    assert s.columns == {"orders.amount": "aaaa:5", "orders.id": "bbbb:5"}
    assert s.schema == sorted(s.schema)
    assert "constraint orders.orders_pkey PRIMARY KEY" in s.schema
    assert any(line.startswith("index orders.orders_pkey CREATE UNIQUE INDEX") for line in s.schema)
    assert len(s.schema) == 4


def test_schema_diff_is_empty_when_down_restores_base_and_ignores_column_order():
    reordered = Snapshot(schema=list(reversed(BASE.schema)), rows={}, columns={})
    reordered.schema.sort()
    assert sql.schema_diff(BASE, reordered) == ""


def test_schema_diff_names_the_column_a_down_migration_forgot():
    diff = sql.schema_diff(BASE, UP_ADD)
    assert "+column orders.discount_cents" in diff
    assert "schema after down" in diff


def test_schema_diff_catches_a_changed_default():
    after = snap({"orders.amount": "aaaa:5", "orders.id": "bbbb:5"})
    after.schema = [s.replace("orders.amount integer nullable=NO default=",
                              "orders.amount integer nullable=NO default=0") for s in after.schema]
    assert "default=0" in sql.schema_diff(BASE, after)


def test_lost_rows_flags_shrunk_and_vanished_tables_only():
    before = Snapshot(rows={"orders": 5, "audit": 2, "same": 1})
    after = Snapshot(rows={"orders": 3, "same": 4})
    assert sql.lost_rows(before, after) == {"audit": (2, 0), "orders": (5, 3)}
    assert sql.lost_rows(before, before) == {}


def test_requested_renames_reads_the_requirement_text():
    assert sql.requested_renames(REQ_RENAME) == [("amount", "amount_cents")]
    assert sql.requested_renames("Add a nullable `discount_cents` column") == []


def test_rename_check_passes_when_values_move_and_fails_on_drop_and_add():
    renamed = snap({"orders.amount_cents": "aaaa:5", "orders.id": "bbbb:5"})
    dropped = snap({"orders.amount_cents": "cfcd:5", "orders.id": "bbbb:5"})
    [ok] = sql.rename_checks(BASE, renamed, [("amount", "amount_cents")])
    [lost] = sql.rename_checks(BASE, dropped, [("amount", "amount_cents")])
    assert ok["preserved"] and ok["column"] == "orders.amount -> orders.amount_cents"
    assert not lost["preserved"]
    [missing] = sql.rename_checks(BASE, renamed, [("total", "total_cents")])
    assert not missing["preserved"] and missing["base"] == "missing"


def test_plan_pairs_up_and_down_and_spots_missing_downs_and_edited_migrations():
    base = ["migrations/001_a.up.sql", "migrations/001_a.down.sql", "queries/r.sql"]
    head = base + ["migrations/002_b.up.sql", "migrations/002_b.down.sql",
                   "migrations/003_c.up.sql"]
    p = sql.plan(base, head, ["migrations/001_a.up.sql", "migrations/002_b.up.sql"])
    assert p.base_ups == ["migrations/001_a.up.sql"]
    assert p.new == [("migrations/002_b.up.sql", "migrations/002_b.down.sql"),
                     ("migrations/003_c.up.sql", None)]
    assert p.queries == ["queries/r.sql"]
    assert p.edited == ["migrations/001_a.up.sql"]


# evaluate(): snapshots -> Evidence -> Findings --------------------------------------------------


def test_good_change_produces_only_green_evidence_and_no_finding():
    tr = trace(snaps={"base": BASE, "up": UP_ADD, "down": BASE},
               queries=[("queries/report.sql", 0)])
    results = sql.evaluate(tr, [])
    assert {ev.id: ev.result for ev, _ in results} == {
        "sql-reversible": "pass", "sql-rows": "pass", "sql-queries": "pass"}
    assert sql.findings_for(results) == []


def test_down_that_forgets_the_column_is_not_reversible_and_becomes_a_verified_finding():
    results = sql.evaluate(trace(snaps={"base": BASE, "up": UP_ADD, "down": UP_ADD}), [])
    ev, loc = by_id(results)["sql-reversible"]
    assert ev.result == "fail" and "not reversible" in ev.title
    assert "+column orders.discount_cents" in ev.details["schema_diff"]
    assert loc == "migrations/002_x.down.sql"
    [f] = sql.findings_for(results)
    assert f.verified and f.evidence_ids == ["sql-reversible"]
    assert f.lens == "compatibility" and f.severity == "high"


def test_rows_lost_across_up_fails_row_preservation():
    up = snap({"orders.amount": "aaaa:3", "orders.id": "bbbb:3"}, rows={"orders": 3})
    ev, _ = by_id(sql.evaluate(trace(snaps={"base": BASE, "up": up, "down": BASE}), []))["sql-rows"]
    assert ev.result == "fail" and "orders 5 -> 3" in ev.title
    assert ev.details["row_counts"] == [
        {"table": "orders", "base": 5, "after_up": 3, "after_down": 5}]


def test_rename_as_drop_and_add_fails_values_even_when_schema_round_trips():
    up = snap({"orders.amount_cents": "cfcd:5", "orders.id": "bbbb:5"})
    results = sql.evaluate(trace(snaps={"base": BASE, "up": up, "down": BASE}),
                           sql.requested_renames(REQ_RENAME))
    found = by_id(results)
    assert found["sql-reversible"][0].result == "pass"
    ev, loc = found["sql-values"]
    assert ev.result == "fail" and ev.kind == "row-preservation"
    assert loc == "migrations/002_x.up.sql"
    assert [f.evidence_ids for f in sql.findings_for(results)] == [["sql-values"]]


def test_failed_up_missing_down_and_failed_down_each_fail_reversibility():
    up_failed = sql.evaluate(trace(up_codes=(3,), down_codes=(), snaps={"base": BASE}), [])
    assert by_id(up_failed)["sql-reversible"][0].title.startswith("Up migration")
    assert set(by_id(up_failed)) == {"sql-reversible"}  # no snapshot after up: nothing else judged

    no_down = trace(new=[("migrations/002_x.up.sql", None)], down_codes=(),
                    snaps={"base": BASE, "up": UP_ADD})
    ev, loc = by_id(sql.evaluate(no_down, []))["sql-reversible"]
    assert ev.result == "fail" and "no down migration" in ev.title
    assert loc == "migrations/002_x.up.sql"

    down_failed = trace(down_codes=(1,), snaps={"base": BASE, "up": UP_ADD})
    ev, loc = by_id(sql.evaluate(down_failed, []))["sql-reversible"]
    assert ev.result == "fail" and loc == "migrations/002_x.down.sql"


def test_failing_repo_query_fails_and_points_at_the_query():
    tr = trace(snaps={"base": BASE, "up": UP_ADD, "down": BASE},
               queries=[("queries/report.sql", 3)])
    ev, loc = by_id(sql.evaluate(tr, []))["sql-queries"]
    assert ev.result == "fail" and loc == "queries/report.sql"


# Registry, environment, verdict -----------------------------------------------------------------


def test_registry_routes_migrations_to_the_sql_adapter():
    assert adapters.change_type(["migrations/002_x.up.sql", "README.md"]) == "sql"
    assert adapters.change_type(["src/MessagePack/Foo.cs"]) is None
    assert adapters.get("sql") is sql.run
    with pytest.raises(KeyError):
        adapters.get("mobile")


def test_docker_env_is_an_allowlist(monkeypatch):
    monkeypatch.setenv("ANTHROPIC_API_KEY", "sk-ant-fake")
    monkeypatch.setenv("DOCKER_HOST", "unix:///x.sock")
    monkeypatch.setenv("DOCKER_AUTH_TOKEN", "t")
    monkeypatch.setenv("UNRELATED_SETTING", "1")
    env = sql.docker_env()
    assert env["DOCKER_HOST"] == "unix:///x.sock"
    assert not {"ANTHROPIC_API_KEY", "DOCKER_AUTH_TOKEN", "UNRELATED_SETTING"} & set(env)


def _sql_inputs(**kw) -> VerdictInputs:
    path = "migrations/002_x.up.sql"
    f = FileChange(path=path, kind="source", status="added", additions=1, deletions=0,
                   area="db-migrations")
    h = Hunk(id=f"{path}#1", file=path, kind="source", old_start=0, old_lines=0, new_start=1,
             new_lines=1, text="+x")
    intake = IntakeReport(change_id="sql-x", requirement_id="REQ-SQL-1", base="b" * 40, head="x",
                          head_sha="h" * 40, files=[f], hunks=[h],
                          counts={"source": 1, "generated": 0, "total": 1}, summary="")
    routing = RoutingReport(router="sql-rule", routes=[], depth_by_file={path: "deep"})
    return VerdictInputs(intake=intake, routing=routing, requirement_title="t",
                         requirement_text="x", run_ts="ts", adapter="sql",
                         ran=["migration-up-down", "row-preservation"], **kw)


def test_verdict_for_a_sql_change_lists_lenses_but_no_dotnet_gaps():
    r = compose(_sql_inputs(lens_unavailable_reason="not run: deterministic adapter demo"))
    whats = " ".join(n.what for n in r.not_checked)
    assert "logic-edge lens" in whats
    for dotnet in ("mutation", "byte diff", "NativeAOT", "head build", "MessagePack.Tests"):
        assert dotnet not in whats
    assert r.verdict == "needs-sign-off"
    assert "source generator" not in r.residual_risk


def test_verdict_blocks_on_a_verified_sql_finding():
    results = sql.evaluate(trace(snaps={"base": BASE, "up": UP_ADD, "down": UP_ADD}), [])
    r = compose(_sql_inputs(evidence=[ev for ev, _ in results],
                            findings=sql.findings_for(results)))
    assert r.verdict == "blocked" and "F-sql-reversible" in r.verdict_reason


# Integration (real Docker) ----------------------------------------------------------------------


@pytest.mark.skipif(os.environ.get("RUN_DOCKER_TESTS") != "1",
                    reason="set RUN_DOCKER_TESTS=1 to start real Postgres containers")
def test_review_sql_end_to_end_in_docker(tmp_path, monkeypatch):
    from receipts.review_sql import run_review_sql

    repo = tmp_path / "sql-repo.git"
    subprocess.run([str(config.ROOT / "examples" / "make_sql_repo.sh"), str(repo)], check=True,
                   capture_output=True)
    monkeypatch.setattr(config, "RUNS", tmp_path / "runs")
    expected = {"sql-good": ("REQ-SQL-1", "needs-sign-off", set()),
                "sql-bad-down": ("REQ-SQL-1", "blocked", {"sql-reversible"}),
                "sql-data-loss": ("REQ-SQL-2", "blocked", {"sql-values"})}
    for change, (req, verdict, failing) in expected.items():
        assert run_review_sql(repo, change, req) == 0
        change_dir = tmp_path / "runs" / change
        run = change_dir / (change_dir / "latest").read_text().strip()
        receipt = json.loads((run / "receipt.json").read_text())
        evidence = json.loads((run / "evidence.json").read_text())
        assert receipt["verdict"] == verdict, receipt["verdict_reason"]
        assert {e["id"] for e in evidence if e["result"] == "fail"} == failing
        assert {f["evidence_ids"][0] for f in receipt["findings"]} == failing
    left = subprocess.run(["docker", "ps", "-a", "-q", "--filter", f"name={sql.CONTAINER_PREFIX}"],
                          capture_output=True, text=True, check=True).stdout.split()
    assert left == []
