"""Compatibility bytes: build tools/compat-harness against a base and a head MessagePack tree,
diff the serialized bytes per sample case, read base bytes back on head, and diff the public API.

Contract:
    run_compat(base_tree, head_tree, run_dir) -> (list[CommandReceipt], Evidence)
    Evidence(kind="byte-diff", result="pass" | "fail" | "error", details={
        cases: int, differing: [{case, base_hex, head_hex}], roundtrip_source: str,
        roundtrip_failures: [{case, result}], api_added: [str], api_removed: [str],
        build_s: {side: seconds}, stage?: str (only on error)})
    fail = any differing case, any roundtrip failure, or any removed public API line.

Run artefacts: run_dir/logs/<receipt id>.log and run_dir/compat/{base,head}-{bytes,api}.json,
head-roundtrip.json. The per-side harness build lives under WORK/compat-harness/ (binaries never go
into run_dir) and is removed when run_compat returns.
"""

import json
import shutil
import subprocess
import sys
import time
from pathlib import Path

from . import config
from .models import CommandReceipt, Evidence, now_iso
from .runstore import portable
from .sandbox import dotnet_env

HARNESS_SRC = config.TOOLS / "compat-harness"
FIXTURE = HARNESS_SRC / "fixtures" / "base.json"
HARNESS_FILES = ["CompatHarness.csproj", "Directory.Build.props", "Program.cs", "Samples.cs"]


class StepFailed(Exception):
    def __init__(self, receipt: CommandReceipt, stage: str):
        super().__init__(stage)
        self.receipt = receipt
        self.stage = stage


def _run(cmd: list[str], cwd: Path, run_dir: Path, rid: str) -> tuple[CommandReceipt, str]:
    log = run_dir / "logs" / f"{rid}.log"
    log.parent.mkdir(parents=True, exist_ok=True)
    started = now_iso()
    t0 = time.monotonic()
    proc = subprocess.run(cmd, cwd=cwd, capture_output=True, text=True, env=dotnet_env())
    duration = round(time.monotonic() - t0, 2)
    log.write_text(portable(f"$ {' '.join(cmd)}\n# cwd: {cwd}\n# exit: {proc.returncode}\n\n"
                            f"--- stdout ---\n{proc.stdout}\n--- stderr ---\n{proc.stderr}"))
    receipt = CommandReceipt(id=rid, cmd=[portable(c) for c in cmd], cwd=portable(str(cwd)),
                             exit_code=proc.returncode,
                             duration_s=duration, log_path=str(log.relative_to(run_dir)),
                             started_at=started)
    return receipt, proc.stdout


def mp_bin(tree: Path) -> Path:
    # The repo's Directory.Build.props sets BaseOutputPath to <root>/bin/<project>/.
    return Path(tree) / "bin" / "MessagePack" / "Release" / "net8.0"


def harness_root(run_dir: Path) -> Path:
    return config.WORK / "compat-harness" / f"{run_dir.parent.name}-{run_dir.name}"


def build_side(tree: Path, side: str, run_dir: Path, receipts: list[CommandReceipt]) -> Path:
    """Build MessagePack.dll from `tree`, then the harness against it. Returns the harness dll."""
    tree = Path(tree).resolve()
    r, _ = _run([config.DOTNET, "build", "src/MessagePack/MessagePack.csproj", "-f", "net8.0",
                 "-c", "Release", "-nologo", "-v", "q"], tree, run_dir, f"compat-build-mp-{side}")
    receipts.append(r)
    if r.exit_code != 0 or not (mp_bin(tree) / "MessagePack.dll").exists():
        raise StepFailed(r, f"build MessagePack ({side})")

    work = harness_root(run_dir) / side
    work.mkdir(parents=True, exist_ok=True)
    for name in HARNESS_FILES:
        shutil.copy2(HARNESS_SRC / name, work / name)
    r, _ = _run([config.DOTNET, "build", "-c", "Release", "-nologo", "-v", "q",
                 f"-p:MessagePackBin={mp_bin(tree)}", "-o", "out"], work, run_dir,
                f"compat-build-harness-{side}")
    receipts.append(r)
    dll = work / "out" / "CompatHarness.dll"
    if r.exit_code != 0 or not dll.exists():
        raise StepFailed(r, f"build harness ({side})")
    return dll


def harness_json(dll: Path, args: list[str], side: str, label: str, run_dir: Path,
                 receipts: list[CommandReceipt]):
    r, out = _run([config.DOTNET, str(dll), *args], dll.parent, run_dir, f"compat-{label}-{side}")
    receipts.append(r)
    if r.exit_code != 0:
        raise StepFailed(r, f"run harness {label} ({side})")
    data = json.loads(out)
    (run_dir / "compat" / f"{side}-{label}.json").write_text(json.dumps(data, indent=2))
    return data


def diff_bytes(base: dict[str, str], head: dict[str, str]) -> list[dict]:
    """Cases whose hex differs, including cases present on only one side (hex = None)."""
    out = []
    for case in sorted(set(base) | set(head)):
        b, h = base.get(case), head.get(case)
        if b != h:
            out.append({"case": case, "base_hex": b, "head_hex": h})
    return out


def roundtrip_failures(results: dict[str, str]) -> list[dict]:
    return [{"case": c, "result": r} for c, r in sorted(results.items())
            if r != "ok" and not r.startswith("skipped:")]


def diff_api(base: list[str], head: list[str]) -> tuple[list[str], list[str]]:
    b, h = set(base), set(head)
    return sorted(h - b), sorted(b - h)


def public_api_listing(tree: Path, run_dir: Path | None = None) -> list[str]:
    """Public/protected API lines of MessagePack.dll + MessagePack.Annotations.dll built from `tree`."""
    run_dir = Path(run_dir).resolve() if run_dir else config.WORK / "compat-api" / Path(tree).name
    (run_dir / "logs").mkdir(parents=True, exist_ok=True)
    receipts: list[CommandReceipt] = []
    dll = build_side(tree, "tree", run_dir, receipts)
    return harness_json(dll, ["--api"], "tree", "api", run_dir, receipts)


def compose(byte_diff: list[dict], rt_fail: list[dict], api_added: list[str],
            api_removed: list[str], cases: int) -> tuple[str, str, str]:
    """(result, title, excerpt) from the three comparisons."""
    result = "fail" if (byte_diff or rt_fail or api_removed) else "pass"
    title = (f"Compatibility bytes: {len(byte_diff)}/{cases} cases differ, "
             f"{len(rt_fail)} base payloads fail to read on head, "
             f"public API +{len(api_added)}/-{len(api_removed)}")
    lines = [f"{d['case']}: base {d['base_hex']} -> head {d['head_hex']}" for d in byte_diff[:5]]
    lines += [f"roundtrip {f['case']}: {f['result']}" for f in rt_fail[:5]]
    lines += [f"API removed: {a}" for a in api_removed[:5]]
    lines += [f"API added: {a}" for a in api_added[:5]]
    return result, title, "\n".join(lines)


def run_compat(base_tree: Path, head_tree: Path, run_dir: Path,
               evidence_id: str = "compat-bytes") -> tuple[list[CommandReceipt], Evidence]:
    run_dir = Path(run_dir).resolve()
    (run_dir / "compat").mkdir(parents=True, exist_ok=True)
    receipts: list[CommandReceipt] = []
    build_s: dict[str, float] = {}
    try:
        return _run_compat(base_tree, head_tree, run_dir, evidence_id, receipts, build_s)
    finally:
        shutil.rmtree(harness_root(run_dir), ignore_errors=True)


def _run_compat(base_tree: Path, head_tree: Path, run_dir: Path, evidence_id: str,
                receipts: list[CommandReceipt],
                build_s: dict[str, float]) -> tuple[list[CommandReceipt], Evidence]:
    try:
        sides = {}
        for side, tree in (("base", base_tree), ("head", head_tree)):
            t0 = time.monotonic()
            sides[side] = build_side(Path(tree), side, run_dir, receipts)
            build_s[side] = round(time.monotonic() - t0, 2)
        base_bytes = harness_json(sides["base"], [], "base", "bytes", run_dir, receipts)
        head_bytes = harness_json(sides["head"], [], "head", "bytes", run_dir, receipts)
        base_api = harness_json(sides["base"], ["--api"], "base", "api", run_dir, receipts)
        head_api = harness_json(sides["head"], ["--api"], "head", "api", run_dir, receipts)

        # Stored old data is the committed fixture; fall back to this run's base bytes.
        rt_input = run_dir / "compat" / "roundtrip-input.json"
        if FIXTURE.exists():
            shutil.copy2(FIXTURE, rt_input)
            rt_source = "tools/compat-harness/fixtures/base.json"
        else:
            rt_input.write_text(json.dumps(base_bytes, indent=2))
            rt_source = "base build in this run"
        rt = harness_json(sides["head"], ["--roundtrip", str(rt_input)], "head", "roundtrip",
                          run_dir, receipts)
    except StepFailed as e:
        return receipts, Evidence(
            id=evidence_id, kind="byte-diff", title=f"Compatibility bytes: {e.stage} failed",
            result="error", command_ids=[r.id for r in receipts],
            details={"stage": e.stage, "log_path": e.receipt.log_path, "build_s": build_s},
            excerpt=(run_dir / e.receipt.log_path).read_text()[-1500:])

    differing = diff_bytes(base_bytes, head_bytes)
    rt_fail = roundtrip_failures(rt)
    api_added, api_removed = diff_api(base_api, head_api)
    result, title, excerpt = compose(differing, rt_fail, api_added, api_removed, len(base_bytes))
    return receipts, Evidence(
        id=evidence_id, kind="byte-diff", title=title, result=result,
        command_ids=[r.id for r in receipts],
        details={"cases": len(base_bytes), "differing": differing, "roundtrip_source": rt_source,
                 "roundtrip_cases": len(rt), "roundtrip_failures": rt_fail,
                 "api_added": api_added, "api_removed": api_removed, "build_s": build_s},
        excerpt=excerpt)


def main(argv: list[str]) -> int:
    """python -m receipts.compat <base_tree> <head_tree> <run_dir> [--write-fixture]"""
    base, head, run_dir = (Path(a) for a in argv[:3])
    receipts, ev = run_compat(base, head, run_dir)
    (run_dir / "compat" / "evidence.json").write_text(ev.model_dump_json(indent=2))
    (run_dir / "compat" / "commands.json").write_text(
        json.dumps([r.model_dump(mode="json") for r in receipts], indent=2))
    if "--write-fixture" in argv[3:] and (run_dir / "compat" / "base-bytes.json").exists():
        FIXTURE.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(run_dir / "compat" / "base-bytes.json", FIXTURE)
    print(ev.title)
    print(ev.excerpt)
    return 0 if ev.result == "pass" else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
