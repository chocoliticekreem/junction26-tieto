"""Run the routers and the six lenses on a sample change and print what they found.

Records fixtures under fixtures/llm/ on a real run; with MOCK_LLM=1 it replays them.

    uv run python scripts/record_track_c.py --change ju26-issue-2283 --repo <sample repo> [--routers] [--lenses]

Hunks come from receipts.intake.parse_diff when that module exists, else from a vendored copy of the
same parser so prompts (and therefore fixture keys) match what the review pipeline sends.
Requirement text: requirements/<REQ>.md if present, else scripts/track_c_requirements.json (the
upstream issue text).
"""

import argparse
import json
import re
import subprocess
import sys
import time
from pathlib import Path

from receipts import config
from receipts.lenses import default_test_dir_examples, run_all_lenses
from receipts.models import FileChange, Hunk

HERE = Path(__file__).resolve().parent

# --- vendored from receipts/intake.py (Track A) until it is merged -----------------------------
AREA_TABLE = [
    ("src/MessagePack.SourceGenerator/Analyzers/", "analyzer"),
    ("src/MessagePack.SourceGenerator/CodeAnalysis/", "type-collector"),
    ("src/MessagePack.SourceGenerator/Transforms/", "codegen-templates"),
    ("src/MessagePack.SourceGenerator/", "source-generator"),
    ("src/MessagePack.Analyzers.CodeFixes/", "analyzer"),
    ("src/MessagePack.Annotations/", "public-api"),
    ("src/MessagePack/MessagePackSecurity", "security"),
    ("src/MessagePack/Formatters/", "serializer-format"),
    ("src/MessagePack/Resolvers/", "serializer-format"),
    ("src/MessagePack/", "serializer-runtime"),
]


def _classify(p: str) -> str:
    name = p.rsplit("/", 1)[-1]
    if name.endswith(".g.cs") or ".verified." in name or any("snapshot" in s.lower() for s in p.split("/")[:-1]):
        return "generated"
    if p.startswith("tests/"):
        return "test"
    if name.lower().endswith(".md") or p.startswith("doc/"):
        return "docs"
    if name.endswith(".tt"):
        return "template"
    return "source"


def _area_for(path: str, kind: str) -> str:
    if path.startswith("tests/"):
        return "tests"
    if kind == "docs":
        return "docs"
    best, area = "", "other"
    for prefix, a in AREA_TABLE:
        if path.startswith(prefix) and len(prefix) > len(best):
            best, area = prefix, a
    return area


_HUNK_RE = re.compile(r"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@ ?(.*)$")


def _parse_diff_vendored(diff_text: str) -> tuple[list[FileChange], list[Hunk]]:
    files, hunks = [], []
    for block in re.split(r"^(?=diff --git )", diff_text, flags=re.M):
        if not block.startswith("diff --git "):
            continue
        lines = block.split("\n")
        status, new_path, body_start = "modified", None, len(lines)
        for i, line in enumerate(lines[1:], start=1):
            if line.startswith("@@"):
                body_start = i
                break
            if line.startswith("new file mode"):
                status = "added"
            elif line.startswith("deleted file mode"):
                status = "deleted"
            elif line.startswith("+++ ") and line[4:].rstrip("\t") != "/dev/null":
                p = line[4:].rstrip("\t")
                new_path = p[2:] if p[:2] in ("a/", "b/") else p
        path = new_path or lines[0].split(" b/", 1)[-1]
        kind = _classify(path)
        adds = dels = n = 0
        cur, body = None, []

        def flush():
            if cur is not None:
                hunks.append(Hunk(text="\n".join(body), **cur))

        for line in lines[body_start:]:
            m = _HUNK_RE.match(line)
            if m:
                flush()
                n += 1
                body = []
                cur = dict(id=f"{path}#{n}", file=path, kind=kind, old_start=int(m[1]),
                           old_lines=int(m[2]) if m[2] is not None else 1, new_start=int(m[3]),
                           new_lines=int(m[4]) if m[4] is not None else 1, header=m[5].strip())
                continue
            if cur is None:
                continue
            adds += line.startswith("+")
            dels += line.startswith("-")
            body.append(line)
        if body and body[-1] == "":
            body.pop()
        flush()
        files.append(FileChange(path=path, kind=kind, status=status, additions=adds, deletions=dels,
                                area=_area_for(path, kind)))
    return files, hunks


def parse_diff(diff_text: str):
    try:
        from receipts.intake import parse_diff as real
    except ImportError:
        return _parse_diff_vendored(diff_text)
    return real(diff_text)


# -----------------------------------------------------------------------------------------------


def git(repo: Path, *args: str) -> str:
    return subprocess.run(["git", "-C", str(repo), *args], check=True, capture_output=True, text=True).stdout


def requirement_text(req_id: str) -> str:
    f = config.REQUIREMENTS / f"{req_id}.md"
    if f.exists():
        return f.read_text()
    return json.loads((HERE / "track_c_requirements.json").read_text())[req_id]["text"]


def run_routers(hunks, req_text, files):
    from receipts.routers.jev import JevRouter
    from receipts.routers.llm_router import LLMRouter

    area = {f.path: f.area for f in files}
    report = {}
    for router in (JevRouter(), LLMRouter()):
        rows, t0 = [], time.monotonic()
        for h in hunks:
            try:
                r = router.route(h, req_text, {"area": area[h.file]})
                rows.append(r.model_dump())
            except Exception as e:  # report every failure; the caller decides about fallback
                rows.append({"hunk_id": h.id, "error": f"{type(e).__name__}: {e}"})
        ok = [r for r in rows if "error" not in r]
        tokens = sum(r["cost_tokens"] for r in ok)
        report[router.name] = {
            "hunks": len(rows), "ok": len(ok), "tokens": tokens,
            "cost_usd": round(tokens * router.usd_per_token, 6),
            "sum_ms": sum(r["ms"] for r in ok), "wall_s": round(time.monotonic() - t0, 1), "routes": rows,
        }
        print(f"[{router.name}] {len(ok)}/{len(rows)} hunks, {tokens} tokens, ${report[router.name]['cost_usd']}")
        for r in rows:
            print("   ", r.get("hunk_id"), r.get("risk", "-"), r.get("category", ""), r.get("confidence", ""),
                  r.get("error", ""))
    return report


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--change", required=True)
    ap.add_argument("--req", default=None)
    ap.add_argument("--repo", default=str(config.MOCK_REPO))
    ap.add_argument("--base", default=config.BASE_SHA)
    ap.add_argument("--routers", action="store_true")
    ap.add_argument("--lenses", action="store_true")
    ap.add_argument("--ledger", default=None, help="JSON list of ledger entries to pass to the lenses")
    ap.add_argument("--out", default=None, help="write the full JSON result here")
    a = ap.parse_args()
    repo = Path(a.repo)
    req_id = a.req or config.SAMPLE_BRANCHES[a.change]
    head = a.change if git(repo, "branch", "--list", a.change).strip() else f"origin/{a.change}"
    diff = git(repo, "-c", "core.quotePath=false", "diff", "--no-color", "--no-ext-diff", "-M",
               "--src-prefix=a/", "--dst-prefix=b/", a.base, head)
    files, hunks = parse_diff(diff)
    reviewed = [h for h in hunks if h.kind != "generated"]
    print(f"{a.change}: {len(files)} files, {sum(f.kind != 'generated' for f in files)} not generated, "
          f"{len(reviewed)} reviewable hunks")
    req_text = requirement_text(req_id)
    result: dict = {"change": a.change, "req": req_id}
    routes = []
    if a.routers:
        result["routers"] = run_routers(reviewed, req_text, files)
    if a.lenses:
        examples = default_test_dir_examples(lambda p: git(repo, "show", f"{a.base}:{p}"))
        ledger = json.loads(Path(a.ledger).read_text()) if a.ledger else []
        out = run_all_lenses(req_id, req_text, reviewed, routes, ledger, examples, change_id=a.change,
                             areas={f.path: f.area for f in files})
        result["lenses"] = out.to_json()
        for r in out.runs:
            print(f"[{r.lens}] {r.status} {r.model} in={r.input_tokens} out={r.output_tokens} "
                  f"${r.cost_usd} {r.ms}ms fixture={r.from_fixture} hunks={len(r.hunk_ids)} {r.detail[:200]}")
        for f in out.findings + out.hunches + out.citations:
            print(f"  - {f.id} [{f.severity}] ({f.receipt_plan.kind}) {f.location}: {f.claim}")
        for t in out.generated_tests:
            print(f"  * test {t.path} {t.expects} {t.test_names}")
        for q in out.questions:
            print(f"  ? {q.lens}/{q.area}: {q.text} {q.options}")
        if out.ambiguity:
            for i in out.interpretations:
                print(f"  {i.label}: {i.summary}")
        print(f"total lens cost ${out.total_cost_usd}")
    if a.out:
        Path(a.out).write_text(json.dumps(result, indent=2, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
