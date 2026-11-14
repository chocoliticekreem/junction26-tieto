"""Blast radius (notes/blast-radius.md): which EXISTING members a change modifies, who else calls
them, and whether any test exercises those callers.

  analyze(...)          runs tools/impact (Roslyn syntax trees, no semantic model) on base and head
  blast_floor(h, raw)   the RuleRouter floor for one hunk:
                          inside a modified existing member with >= 1 caller outside the change -> 8
                          inside a deleted member that still has callers                        -> 9
                          new members                                                         -> none
  collect_coverage(...) the SourceGenerator test project on HEAD with coverlet -> {file: {line: hits}}
  mark_tested(raw, hits) / report(...) -> impact.json (shape fixed: the dashboard reads it)

Call sites are matched by NAME (plus argument count, plus the receiver's type when it is visible),
so common names over-approximate; `ambiguous` says when that happened. A caller is "in the change"
when it sits inside a member this change adds or modifies; an unchanged member of a touched file is
old behaviour relying on the target, so it counts as outside.
"""

import hashlib
import json
import shutil
import xml.etree.ElementTree as ET
from pathlib import Path

from . import config, sandbox
from .models import CommandReceipt, Hunk, NotChecked
from .runstore import portable_file

METHOD = "name-based call sites (Roslyn syntax, not semantic)"
MODIFIED_FLOOR = 8
DELETED_FLOOR = 9
TOOL_SRC = config.TOOLS / "impact"
TOOL_FILES = ["Impact.csproj", "Directory.Build.props", "Program.cs"]
COVERLET_REF = '<PackageReference Include="coverlet.collector" VersionOverride="6.0.2" />'
COVERLET_ANCHOR = '<PackageReference Include="xunit" />'
COVERAGE_SCOPE = "tests/MessagePack.SourceGenerator.Tests"


# Tool -----------------------------------------------------------------------------------------


def _tool_dir() -> Path:
    return config.WORK / "impact-tool"


def _source_hash() -> str:
    h = hashlib.sha256()
    for name in TOOL_FILES:
        h.update((TOOL_SRC / name).read_bytes())
    return h.hexdigest()[:16]


def ensure_tool(run_dir: Path, commands: list[CommandReceipt]) -> Path | None:
    """Build tools/impact once per source version under WORK (bin/obj never land in the repo)."""
    out = _tool_dir()
    dll = out / "Impact.dll"
    stamp = out / "source.sha"
    want = _source_hash()
    with sandbox._flock(config.WORK / ".locks" / "impact-tool.lock"):
        if dll.exists() and stamp.exists() and stamp.read_text() == want:
            return dll
        src = config.WORK / "impact-tool-src"
        src.mkdir(parents=True, exist_ok=True)
        for name in TOOL_FILES:
            shutil.copy2(TOOL_SRC / name, src / name)
        r = sandbox.run_cmd("impact-build", [config.DOTNET, "build", "-c", "Release", "-nologo",
                                             "-v", "q", "-o", str(out)], src, run_dir, 600)
        commands.append(r)
        if r.exit_code != 0 or not dll.exists():
            return None
        stamp.write_text(want)
        return dll


def analyze(changed_paths: list[str], base_wt: Path, head_wt: Path, run_dir: Path,
            commands: list[CommandReceipt]) -> tuple[dict | None, str]:
    """(raw tool output, "" ) or (None, why it did not run)."""
    dll = ensure_tool(run_dir, commands)
    if dll is None:
        return None, "the impact tool did not build (see logs/impact-build.log)"
    d = run_dir / "impact"
    d.mkdir(parents=True, exist_ok=True)
    (d / "changed.txt").write_text("\n".join(changed_paths) + "\n")
    out = d / "members.json"
    r = sandbox.run_cmd("impact", [config.DOTNET, str(dll), "--base", str(base_wt), "--head",
                                   str(head_wt), "--changed", str(d / "changed.txt"), "--out",
                                   str(out)], run_dir, run_dir, 600)
    commands.append(r)
    if r.exit_code != 0 or not out.exists():
        return None, f"the impact tool failed (exit {r.exit_code}, logs/impact.log)"
    portable_file(out)
    return json.loads(out.read_text()), ""


# Rule -----------------------------------------------------------------------------------------


def changed_lines(h: Hunk) -> tuple[set[int], set[int]]:
    """(added line numbers on head, deleted line numbers on base) of one hunk; context excluded."""
    new, old = h.new_start, h.old_start
    added: set[int] = set()
    deleted: set[int] = set()
    for ln in h.text.split("\n"):
        if ln.startswith("\\"):
            continue
        if ln.startswith("+"):
            added.add(new)
            new += 1
        elif ln.startswith("-"):
            deleted.add(old)
            old += 1
        else:
            new += 1
            old += 1
    return added, deleted


def _within(lines: set[int], span) -> bool:
    return bool(span) and any(span[0] <= n <= span[1] for n in lines)


def members_for_hunk(h: Hunk, raw: dict | None) -> list[dict]:
    if not raw:
        return []
    added, deleted = changed_lines(h)
    return [m for m in raw.get("members", []) if m["file"] == h.file
            and (_within(added, m.get("head_span")) or _within(deleted, m.get("base_span")))]


def outside_callers(m: dict) -> list[dict]:
    return [c for c in m.get("callers", []) if not c["in_change"]]


def short_name(m: dict) -> str:
    member = m.get("member") or m["name"]
    type_name = m.get("type_name", "")
    if member in (".ctor", ".cctor"):
        return f"{type_name} constructor"
    return f"{type_name}.{member}" if type_name else m["name"]


def blast_floor(h: Hunk, raw: dict | None) -> tuple[int, str] | None:
    """(risk floor, reason) for a source hunk inside an existing member that other code uses."""
    if h.kind not in ("source", "template"):
        return None
    hits = members_for_hunk(h, raw)
    # Head compiles, so a remaining call by that name outside the change binds to something else
    # (another overload, a base member) whose behaviour this deletion may have shifted.
    deleted = [m for m in hits if m["kind"] == "deleted" and outside_callers(m)]
    if deleted:
        m = max(deleted, key=lambda x: len(outside_callers(x)))
        return DELETED_FLOOR, (f"deletes existing {short_name(m)} whose name is still used in "
                               f"{len(outside_callers(m))} other places")
    modified = [m for m in hits if m["kind"] == "modified" and outside_callers(m)]
    if modified:
        m = max(modified, key=lambda x: len(outside_callers(x)))
        return MODIFIED_FLOOR, (f"modifies existing {short_name(m)} used in "
                                f"{len(outside_callers(m))} other places")
    return None


# Coverage -------------------------------------------------------------------------------------


def parse_cobertura(path: Path) -> dict[str, dict[int, int]]:
    hits: dict[str, dict[int, int]] = {}
    for cls in ET.parse(path).getroot().iter("class"):
        per = hits.setdefault(cls.get("filename", "").replace("\\", "/"), {})
        for line in cls.iter("line"):
            n, c = int(line.get("number")), int(line.get("hits", "0"))
            per[n] = max(per.get(n, 0), c)
    return hits


def collect_coverage(head_sha: str, run_dir: Path, commands: list[CommandReceipt],
                     keep_files: set[str]) -> tuple[dict | None, str]:
    """Run the SourceGenerator tests on HEAD with coverlet in a private worktree. coverlet.collector
    is added to that worktree's csproj for this run only (never to the change under review) and the
    worktree is reset afterwards. Returns ({file: {line: hits}} for keep_files, "") or (None, why)."""
    wt = sandbox.worktree("cov", head_sha, name=f"cov-{head_sha[:10]}")
    results = run_dir / "logs" / "coverage-head-results"
    with sandbox.lock(wt):
        try:
            proj = wt / config.TEST_PROJECT
            text = proj.read_text()
            if "coverlet.collector" not in text:
                if COVERLET_ANCHOR not in text:
                    return None, "could not add coverlet.collector to the test project"
                proj.write_text(text.replace(COVERLET_ANCHOR,
                                             f"{COVERLET_ANCHOR}\n    {COVERLET_REF}", 1))
            r = sandbox.run_cmd("coverage-head", [
                config.DOTNET, "test", config.TEST_PROJECT, "-f", "net8.0",
                "--collect:XPlat Code Coverage", "--results-directory", str(results)],
                wt, run_dir, sandbox.TEST_TIMEOUT_S)
            commands.append(r)
        finally:
            sandbox.reset(wt)
    reports = sorted(results.glob("*/coverage.cobertura.xml"))
    if not reports:
        return None, f"coverlet wrote no report (coverage-head exit {r.exit_code})"
    hits = parse_cobertura(reports[0])
    kept = {f: hits.get(f, {}) for f in sorted(keep_files)}
    # The full report is megabytes of lines nobody cites; keep only the caller files' lines.
    shutil.rmtree(results, ignore_errors=True)
    (run_dir / "impact").mkdir(exist_ok=True)
    (run_dir / "impact" / "coverage-lines.json").write_text(json.dumps(
        {f: {str(n): c for n, c in sorted(v.items())} for f, v in kept.items()}, indent=1))
    return kept, ""


def caller_files(raw: dict | None) -> set[str]:
    return {c["file"] for m in (raw or {}).get("members", []) for c in m.get("callers", [])}


def mark_tested(raw: dict, hits: dict | None) -> None:
    """caller["tested"]: some line from the caller's statement start to the call line was executed
    by a test (coverlet line hits). None when coverage could not be collected."""
    for m in raw.get("members", []):
        for c in m.get("callers", []):
            if hits is None:
                c["tested"] = None
                continue
            per = hits.get(c["file"], {})
            c["tested"] = any(per.get(n, 0) > 0
                              for n in range(c.get("statement_start", c["line"]), c["line"] + 1))


# Report ---------------------------------------------------------------------------------------


def report(raw: dict | None, characterization: list[dict], coverage_available: bool) -> dict:
    """impact.json, exactly the shape the dashboard builds against."""
    members = []
    for m in (raw or {}).get("members", []):
        callers = [{"file": c["file"], "line": c["line"], "member": c["member"],
                    "in_change": c["in_change"], "tested": c.get("tested")}
                   for c in m.get("callers", [])]
        outside = [c for c in callers if not c["in_change"]]
        members.append({
            "name": m["name"], "file": m["file"], "kind": m["kind"],
            "signature_changed": bool(m.get("signature_changed")), "callers": callers,
            "callers_outside_change": len(outside),
            "untested_callers": sum(c["tested"] is False for c in outside),
            "ambiguous": bool(m.get("ambiguous")),
        })
    return {
        "method": METHOD,
        "members": members,
        "characterization": characterization,
        "coverage_available": coverage_available,
        "summary": {
            "modified_existing": sum(m["kind"] == "modified" for m in members),
            "new": sum(m["kind"] == "new" for m in members),
            "deleted": sum(m["kind"] == "deleted" for m in members),
            "callers_outside_change": sum(m["callers_outside_change"] for m in members),
            "untested_callers": sum(m["untested_callers"] for m in members),
            "behaviour_changes": sum(c["result"] == "changed" for c in characterization),
        },
    }


def not_checked(raw: dict | None, why_missing: str, coverage_why: str) -> list[NotChecked]:
    if raw is None:
        return [NotChecked(what="blast radius (call sites of modified members)",
                           why=why_missing or "the impact analysis did not run")]
    out = [NotChecked(
        what="semantic call graph of modified members",
        why="call sites are matched by name on syntax trees (Roslyn, no semantic model): common "
            "names over-approximate, and members called only by the framework (a generator's "
            "Initialize, an analyzer's SupportedDiagnostics), by reflection or from other "
            "repositories show no callers")]
    if coverage_why:
        out.append(NotChecked(what="test coverage of callers (legacy coverage gap)",
                              why=f"coverage could not be collected: {coverage_why}; every "
                                  "caller is marked tested: null"))
    for m in raw.get("members", []):
        untested = [c for c in outside_callers(m) if c.get("tested") is False]
        if untested:
            where = ", ".join(f"{c['file']}:{c['line']}" for c in untested)
            out.append(NotChecked(
                what=(f"{len(untested)} callers of {short_name(m)} are not exercised by any test "
                      f"(legacy coverage gap): {where}"),
                why=f"no test in {COVERAGE_SCOPE} executes these call sites on head (coverlet line "
                    "coverage); a behaviour change reaching them would go unnoticed by the suite"))
    return out
