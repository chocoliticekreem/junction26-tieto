"""Paths and environment. Every other module imports locations from here."""

import os
from pathlib import Path

from dotenv import load_dotenv

ROOT = Path(__file__).resolve().parents[2]
load_dotenv(ROOT / ".env")
load_dotenv(ROOT / ".env.local", override=True)

PIPELINE = ROOT / "pipeline"
MOCK_REPO = ROOT / "mock-repo"  # bare clone of the Tieto sample repo
WORK = ROOT / "work"  # git worktrees and build outputs, gitignored
RUNS = ROOT / "runs"
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
