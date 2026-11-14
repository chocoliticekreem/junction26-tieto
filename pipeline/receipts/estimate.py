"""Cost estimate for queued LLM requests (llm collect mode), computed before anything is sent.

Input tokens ~ prompt characters / CHARS_PER_TOKEN (system + user + JSON schema). Output tokens per purpose are
the mean of what earlier real calls produced: lens calls from runs/*/*/lenses.json, readers and
the orientation call from runs/*/*/readers.json, anything else from the recorded fixtures. Replayed
entries (from_fixture) are skipped so one real call counts once. Those runs used the historical
effort, so for a lens run at a lower effort the output figure is an upper bound.
"""

import json
from dataclasses import dataclass
from pathlib import Path

from . import config, llm

# Not the usual 4: on the 36 bench cases, chars/4 came out 1.71-1.80x below the input tokens the
# API reported per lens (diff hunks and C# tokenize densely), so the guard would under-estimate.
CHARS_PER_TOKEN = 2.3
DEFAULT_OUTPUT_TOKENS = 2000  # a purpose with no recorded call at all


@dataclass
class Line:
    purpose: str
    model: str
    effort: str
    requests: int
    input_tokens: int
    output_tokens: int
    output_basis: str  # "N recorded calls" or "no history"

    @property
    def standard_usd(self) -> float:
        return llm.cost_usd(self.model, self.input_tokens, self.output_tokens)

    @property
    def batch_usd(self) -> float:
        return round(self.standard_usd * llm.BATCH_DISCOUNT, 6)


def _read(path: Path) -> dict:
    try:
        return json.loads(path.read_text())
    except (OSError, ValueError):
        return {}


def historical_output(run_roots: list[Path] | None = None,
                      fixtures: Path | None = None) -> dict[str, tuple[float, int]]:
    """purpose -> (mean output tokens, number of real calls averaged)."""
    roots = run_roots if run_roots is not None else sorted({config.RUNS, config.ROOT / "runs"})
    samples: dict[str, list[int]] = {}

    def add(purpose: str, rec: dict) -> None:
        if rec.get("output_tokens") and not rec.get("from_fixture"):
            samples.setdefault(purpose, []).append(int(rec["output_tokens"]))

    for root in roots:
        for f in root.glob("*/*/lenses.json"):
            for r in _read(f).get("lenses", []):
                add(f"lens-{r.get('lens')}", r)
        for f in root.glob("*/*/readers.json"):
            data = _read(f)
            for r in data.get("readers", []):
                add("readers", r)
            add("readers-orient", data.get("orientation") or {})
    fixtures = fixtures or config.FIXTURES_LLM
    for d in sorted(fixtures.iterdir()) if fixtures.exists() else []:
        if d.is_dir() and d.name not in samples:
            for f in d.glob("*.json"):
                out = _read(f).get("output_tokens")
                if out:
                    samples.setdefault(d.name, []).append(int(out))
    return {p: (sum(v) / len(v), len(v)) for p, v in samples.items()}


def estimate(requests: dict[str, dict], history: dict[str, tuple[float, int]]) -> list[Line]:
    """One line per (purpose, model, effort) over the queued requests ({custom_id: queue entry})."""
    lines: dict[tuple[str, str, str], Line] = {}
    for e in requests.values():
        p = e["params"]
        chars = len(p.get("system", "")) + sum(len(m["content"]) for m in p["messages"])
        chars += len(json.dumps(p.get("output_config", {}).get("format", {})))
        effort = p.get("output_config", {}).get("effort", "-")
        mean, n = history.get(e["purpose"], (DEFAULT_OUTPUT_TOKENS, 0))
        k = (e["purpose"], p["model"], effort)
        line = lines.setdefault(k, Line(e["purpose"], p["model"], effort, 0, 0, 0,
                                        f"{n} recorded calls" if n else "no history"))
        line.requests += 1
        line.input_tokens += round(chars / CHARS_PER_TOKEN)
        line.output_tokens += min(round(mean), p["max_tokens"])
    return sorted(lines.values(), key=lambda x: -x.standard_usd)


def totals(lines: list[Line]) -> dict:
    return {"requests": sum(x.requests for x in lines),
            "input_tokens": sum(x.input_tokens for x in lines),
            "output_tokens": sum(x.output_tokens for x in lines),
            "standard_usd": round(sum(x.standard_usd for x in lines), 2),
            "batch_usd": round(sum(x.batch_usd for x in lines), 2)}


def to_json(lines: list[Line]) -> dict:
    return {"lines": [{"purpose": x.purpose, "model": x.model, "effort": x.effort,
                       "requests": x.requests, "input_tokens": x.input_tokens,
                       "output_tokens": x.output_tokens, "output_basis": x.output_basis,
                       "standard_usd": round(x.standard_usd, 4), "batch_usd": round(x.batch_usd, 4)}
                      for x in lines], "totals": totals(lines)}


def format_table(lines: list[Line]) -> str:
    head = (f"{'purpose':<24} {'model':<26} {'effort':<7} {'req':>4} {'in tok':>9} {'out tok':>9} "
            f"{'standard $':>11} {'batch $':>9}  output basis")
    rows = [head, "-" * len(head)]
    for x in lines:
        rows.append(f"{x.purpose:<24} {x.model:<26} {x.effort:<7} {x.requests:>4} "
                    f"{x.input_tokens:>9,} {x.output_tokens:>9,} {x.standard_usd:>11.2f} "
                    f"{x.batch_usd:>9.2f}  {x.output_basis}")
    t = totals(lines)
    rows.append(f"{'total':<24} {'':<26} {'':<7} {t['requests']:>4} {t['input_tokens']:>9,} "
                f"{t['output_tokens']:>9,} {t['standard_usd']:>11.2f} {t['batch_usd']:>9.2f}")
    rows.append("standard $ = the same requests sent live (non-batch path); batch $ = Message "
                "Batches at 50%.")
    return "\n".join(rows)


# ---------------------------------------------------------------- Verda projection
#
# Verda has no batch API, so there is no collect queue to price. Instead a review is projected
# from what the same review consumed on Anthropic: the recorded input and output tokens of every
# lens, reader, orientation and A/B call, re-priced at the Verda model of the same role. Open
# models tokenize and reason differently, so output tokens are scaled by the measured ratio
# (Verda output / Anthropic output per role, from `@verda` runs of the same change) once one
# exists; before that the ratio is 1.0 and the projection says so.

DEEP_PURPOSES = {"lens-requirement-tests", "lens-logic-edge", "ab-variant"}


def role_of(purpose: str) -> str:
    if purpose in DEEP_PURPOSES:
        return "deep"
    return "router" if purpose.startswith("router") else "lens"


def run_usage(run_dir: Path) -> list[tuple[str, int, int]]:
    """(purpose, input tokens, output tokens) for every LLM call a run recorded, replayed or not."""
    out: list[tuple[str, int, int]] = []
    for r in _read(run_dir / "lenses.json").get("lenses", []):
        if r.get("input_tokens"):
            out.append((f"lens-{r['lens']}", r["input_tokens"], r.get("output_tokens", 0)))
    readers = _read(run_dir / "readers.json")
    for r in readers.get("readers", []):
        if r.get("input_tokens"):
            out.append(("readers", r["input_tokens"], r.get("output_tokens", 0)))
    orient = readers.get("orientation") or {}
    if orient.get("input_tokens"):
        out.append(("readers-orient", orient["input_tokens"], orient.get("output_tokens", 0)))
    ab = _read(run_dir / "ab.json").get("tokens") or {}
    if ab.get("input"):
        out.append(("ab-variant", ab["input"], ab.get("output", 0)))
    return out


def latest_dir(change_id: str, root: Path | None = None, mode: str = "") -> Path | None:
    """Newest complete run of `change_id`; with `mode`, the newest whose bench.json has it."""
    base = (root or config.RUNS) / change_id
    if not base.is_dir():
        return None
    dirs = sorted(d for d in base.iterdir() if (d / "receipt.json").exists())
    if mode:
        dirs = [d for d in dirs if _read(d / "bench.json").get("mode") == mode]
    return dirs[-1] if dirs else None


def output_ratio(pairs: list[tuple[Path, Path]]) -> dict[str, float]:
    """role -> Verda output / Anthropic output, over (anthropic run, verda run) pairs."""
    sums: dict[str, list[int]] = {}
    for a, v in pairs:
        for side, d in ((0, a), (1, v)):
            for purpose, _, out in run_usage(d):
                sums.setdefault(role_of(purpose), [0, 0])[side] += out
    return {r: round(v / a, 3) for r, (a, v) in sums.items() if a and v}


def project_verda(run_dirs: list[Path], models: dict[str, str],
                  ratio: dict[str, float] | None = None) -> dict:
    """EUR and USD at Verda list prices for re-running `run_dirs` (Anthropic runs) on Verda."""
    ratio = ratio or {}
    by_role: dict[str, list[int]] = {}
    for d in run_dirs:
        for purpose, tin, tout in run_usage(d):
            role = role_of(purpose)
            acc = by_role.setdefault(role, [0, 0, 0])
            acc[0] += 1
            acc[1] += tin
            acc[2] += round(tout * ratio.get(role, 1.0))
    roles = {r: {"model": models[r], "calls": c, "input_tokens": i, "output_tokens": o,
                 "output_ratio": ratio.get(r, 1.0),
                 "usd": llm.cost_usd(models[r], i, o), "eur": llm.cost_eur(models[r], i, o)}
             for r, (c, i, o) in by_role.items()}
    return {"runs": len(run_dirs), "roles": roles,
            "usd": round(sum(x["usd"] for x in roles.values()), 4),
            "eur": round(sum(x["eur"] for x in roles.values()), 4)}


def verda_plans(root: Path | None = None) -> dict:
    """Projected Verda cost of the re-run plan in docs/verda.md, from the recorded Anthropic runs."""
    from .bench import load_manifest

    root = root or config.RUNS
    manifest = load_manifest()
    models = {"deep": config.VERDA_MODEL_DEEP, "lens": config.VERDA_MODEL_LENS,
              "router": config.VERDA_MODEL_ROUTER}
    tieto = [c for b in config.SAMPLE_BRANCHES for c in (b, f"{b}__A", f"{b}__B")]
    pairs = [(a, v) for c in [*tieto, *(e["id"] for e in manifest)]
             if (a := latest_dir(c, root)) and (v := latest_dir(f"{c}@verda", root))]
    ratio = output_ratio(pairs)

    def dirs(ids, mode=""):
        return [d for c in ids if (d := latest_dir(c, root, mode))]

    plans = {
        "a_cold_bench_36": dirs([e["id"] for e in manifest], "cold"),
        "b_warm_mock_2283": dirs([e["id"] for e in manifest if e["issue"] == "2283"], "warm"),
        "b_warm_mock_2234": dirs([e["id"] for e in manifest if e["issue"] == "2234"], "warm"),
        "c_2297_r2_with_ab": dirs(["ju26-issue-2297", "ju26-issue-2297__A", "ju26-issue-2297__B"]),
        "d_tieto_four_with_ab": dirs(tieto),
    }
    return {"models": models, "output_ratio": ratio,
            "ratio_basis": (f"{len(pairs)} anthropic/verda run pairs" if pairs else
                            "no @verda run yet: Verda output tokens assumed equal to Anthropic's"),
            "eur_per_usd": config.VERDA_EUR_PER_USD,
            "plans": {name: project_verda(ds, models, ratio) for name, ds in plans.items()},
            "note": ("c uses the round-1 2297 runs as the token basis (round 2 has no complete "
                     "Anthropic run). Prices: verda.com/inference list prices, cached input at "
                     "the full input price, so these are upper bounds at the given output ratio.")}
