"""Paths and environment. Every other module imports locations from here."""

import os
from pathlib import Path

from dotenv import load_dotenv

ROOT = Path(__file__).resolve().parents[2]
# Precedence: process environment > env file (.env.local or RECEIPTS_ENV_FILE) > .env. Nothing
# overrides the shell, so `MOCK_LLM=1` on the command line can never be undone by a file.
load_dotenv(os.environ.get("RECEIPTS_ENV_FILE", ROOT / ".env.local"), override=False)
load_dotenv(ROOT / ".env", override=False)

PIPELINE = ROOT / "pipeline"
# Env overrides let several git worktrees of this project share one clone and one build cache.
MOCK_REPO = Path(os.environ.get("RECEIPTS_MOCK_REPO") or ROOT / "mock-repo")  # bare clone
WORK = Path(os.environ.get("RECEIPTS_WORK") or ROOT / "work")  # git worktrees, gitignored
RUNS = Path(os.environ.get("RECEIPTS_RUNS") or ROOT / "runs")
CONTEXT = ROOT / "context"
DECISIONS = ROOT / "decisions"
METRICS = ROOT / "metrics"
REQUIREMENTS = ROOT / "requirements"
BENCH = ROOT / "bench"
FIXTURES_LLM = ROOT / "fixtures" / "llm"
NOTES = ROOT / "notes"
TOOLS = ROOT / "tools"

UPSTREAM_URL = "https://github.com/te-industry-tech-office/junction26-messagepack-csharp"
BASE_SHA = "32258fdb"
SAMPLE_CLONE = Path(
    os.environ.get("RECEIPTS_SAMPLE_CLONE")
    or "/private/tmp/claude-501/-Users-sonionring-orca-projects-junction/"
    "aae424c1-cec2-4b34-862a-bf3153e7d7ee/scratchpad/tieto/repo"
)
SAMPLE_BRANCHES = {
    "ju26-issue-2283": "REQ-2283",
    "ju26-issue-2234": "REQ-2234",
    "ju26-issue-2129": "REQ-2129",
    "ju26-issue-2297": "REQ-2297",
}

DOTNET = os.path.expanduser(os.environ.get("DOTNET") or "~/.dotnet/dotnet")
DOTNET_ENV = {"DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_NOLOGO": "1"}
TEST_PROJECT = "tests/MessagePack.SourceGenerator.Tests/MessagePack.SourceGenerator.Tests.csproj"

ANTHROPIC_API_KEY = os.environ.get("ANTHROPIC_API_KEY", "")
JEV_API_KEY = os.environ.get("JEV_API_KEY", "")
MOCK_LLM = os.environ.get("MOCK_LLM", "0") == "1"

MODEL_DEEP = "claude-opus-5-5"  # requirement-tests, logic-edge
MODEL_LENS = "claude-sonnet-5-5"  # other lenses
MODEL_ROUTER = "claude-haiku-4-5-20251001"  # LLMRouter

QUESTION_BUDGET = 3
MUTATION_RISK_THRESHOLD = 5
MUTANTS_PER_HUNK = 5
ROUTER_DEEP_THRESHOLD = 5  # risk >= this gets the expensive lenses

EFFORTS = ("low", "medium", "high", "xhigh", "max")
# The effort every lens ran at when the committed fixtures were recorded. llm.call_json leaves effort
# out of the fixture key at these values, so those fixtures keep replaying.
LENS_EFFORT_DEFAULT = {
    "requirement-tests": "high",
    "logic-edge": "high",
    "compatibility": "medium",
    "security": "medium",
    "product-context": "medium",
    "test-quality": "medium",
}


def parse_lens_effort(spec: str) -> dict[str, str]:
    """`"requirement-tests=medium,logic-edge=medium"` -> {lens: effort}. Unknown lens or effort raises."""
    out: dict[str, str] = {}
    for part in filter(None, (p.strip() for p in spec.split(","))):
        lens, sep, effort = (x.strip() for x in part.partition("="))
        if not sep or lens not in LENS_EFFORT_DEFAULT or effort not in EFFORTS:
            raise ValueError(f"RECEIPTS_LENS_EFFORT: bad entry {part!r}; expected <lens>=<effort>, "
                             f"lens in {sorted(LENS_EFFORT_DEFAULT)}, effort in {list(EFFORTS)}")
        out[lens] = effort
    return out


LENS_EFFORT = {**LENS_EFFORT_DEFAULT, **parse_lens_effort(os.environ.get("RECEIPTS_LENS_EFFORT", ""))}

# "" calls the API on a fixture miss. "collect" queues the request (see llm.PendingLLM) and never
# calls; "replay" answers from fixtures only, for Anthropic calls (Jev is untouched by both).
LLM_MODE = os.environ.get("RECEIPTS_LLM_MODE", "")
if LLM_MODE not in ("", "collect", "replay"):
    raise ValueError(f"RECEIPTS_LLM_MODE={LLM_MODE!r}: expected unset, 'collect' or 'replay'")
BATCH_DIR = Path(os.environ.get("RECEIPTS_BATCH_DIR") or WORK / "batch")
