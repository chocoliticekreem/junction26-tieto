"""A/B decisions (SPEC 5.7): when the reading panel (lenses.readers) votes that the requirement has
two plausible readings, build both as branches.

  review/<change>/A   the original head (reading A, what the change implements)
  review/<change>/B   a minimal edit on top of the original head implementing reading B, written by
                      one Opus call (purpose "ab-variant"), applied in a private worktree, required to
                      build and pass the SourceGenerator tests (one retry with the errors), committed
                      as "Receipts reviewer <reviewer@receipts.local>"

review.run_review then reviews both branches as change ids <change>__A and <change>__B.
The AI never picks: the refusal quotes the requirement text that supports both readings.
"""

import json
import os
import subprocess
from dataclasses import dataclass, field
from pathlib import Path

from . import config, llm, sandbox
from .checks import compile_errors
from .gitutil import git, rev_parse
from .models import Evidence, Interpretation, IntakeReport, NotChecked, Refusal, TestRun

AUTHOR_NAME = "Receipts reviewer"
AUTHOR_EMAIL = "reviewer@receipts.local"
MAX_ATTEMPTS = 2
FILE_KINDS = {"source", "template", "test"}
# Where a new generator setting has to be plumbed in this repo: the resolver attribute users set,
# the options record the templates read, and the attribute parser. Shown in full to the variant call.
SETTINGS_FILES = [
    "src/MessagePack/GeneratedMessagePackResolverAttribute.cs",
    "src/MessagePack.SourceGenerator/CodeAnalysis/AnalyzerOptions.cs",
    "src/MessagePack.SourceGenerator/Utils/AnalyzerUtilities.cs",
]
PUBLIC_API_GLOB = "src/MessagePack/*/PublicAPI.Unshipped.txt"
PUBLIC_API_ANCHOR = "GeneratedMessagePackResolverAttribute"

SYSTEM = """You are the A/B builder in "Receipts", an auditable AI code review of a change to
MessagePack-CSharp. The requirement admits two readings. The change under review implements reading
A. Your job: write the MINIMAL edit on top of that change that implements reading B instead, so a
named human can compare two working branches and merge one. You do not judge which reading is right.

Rules:
- Edit only what reading B needs. Keep everything else of the change as it is.
- Keep the default behaviour identical to reading A unless reading B itself requires a different
  default. The generator's output for existing tests is compared byte for byte against verified
  snapshot files you cannot see; any change to default output breaks them.
- Templates come in pairs: a T4 source (*.tt) and its checked-in generated C# (*Template.cs, which
  writes text with this.Write("...") calls and literal \\r\\n escapes). Change both consistently.
- A new public member of src/MessagePack must be listed in every
  src/MessagePack/<tfm>/PublicAPI.Unshipped.txt (net472, net8.0, netstandard2.0, netstandard2.1);
  excerpts of those files are given.
- You may add one focused xUnit test for the B-specific behaviour to a test file the change already
  touches, using only helpers that file already uses.
- Code must compile with C# 12 / net8.0 and the repo's analyzers (StyleCop: `this.` prefixes, XML docs
  on public members, file header). Re-read every edit before answering.

Output edits as exact search/replace pairs:
- search: an exact, unique excerpt of the current file (copy it verbatim from the files shown, at
  least one full line, enough lines to be unique); replace: its new text. Line endings do not matter.
- search "" creates a new file whose content is replace (the path must not exist).
- summary: one short line describing reading B as implemented (used in the commit message).
- quote_supporting_both: an exact excerpt of the requirement text (verbatim, at most two sentences)
  that shows why both readings are supported.
"""

SCHEMA = {
    "type": "object",
    "properties": {
        "summary": {"type": "string"},
        "quote_supporting_both": {"type": "string"},
        "edits": {
            "type": "array",
            "items": {
                "type": "object",
                "properties": {
                    "path": {"type": "string"},
                    "search": {"type": "string"},
                    "replace": {"type": "string"},
                },
                "required": ["path", "search", "replace"],
                "additionalProperties": False,
            },
        },
    },
    "required": ["summary", "quote_supporting_both", "edits"],
    "additionalProperties": False,
}


class EditError(ValueError):
    pass


@dataclass
class ABResult:
    branches: dict[str, str] = field(default_factory=dict)  # label -> branch ("" if not built)
    shas: dict[str, str] = field(default_factory=dict)
    evidence: list[Evidence] = field(default_factory=list)
    not_checked: list[NotChecked] = field(default_factory=list)
    refusal: Refusal | None = None
    summary_b: str = ""
    diff_stat: str = ""
    cost_usd: float = 0.0
    tokens: dict = field(default_factory=dict)


def branch_name(change_id: str, label: str) -> str:
    return f"review/{change_id}/{label}"


def _read(wt: Path, rel: str) -> str:
    return (wt / rel).read_bytes().decode("utf-8").lstrip("﻿").replace("\r\n", "\n")


def context_files(intake: IntakeReport, wt: Path) -> list[tuple[str, str]]:
    """(path, LF-normalised content) of the change's non-generated code and test files, then the
    settings plumbing. Docs are left out: their hunks are in the prompt."""
    paths = [f.path for f in intake.files
             if f.kind in FILE_KINDS and f.status != "deleted" and (wt / f.path).exists()]
    paths += [p for p in SETTINGS_FILES if p not in paths and (wt / p).exists()]
    return [(p, _read(wt, p)) for p in paths]


def public_api_excerpts(wt: Path) -> list[tuple[str, str]]:
    out = []
    for p in sorted(wt.glob(PUBLIC_API_GLOB)):
        lines = [ln for ln in _read(wt, str(p.relative_to(wt))).splitlines()
                 if PUBLIC_API_ANCHOR in ln]
        if lines:
            out.append((str(p.relative_to(wt)), "\n".join(lines)))
    return out


def build_prompt(req_id: str, req_text: str, interps: list[Interpretation], intake: IntakeReport,
                 wt: Path) -> str:
    by = {i.label: i.summary for i in interps}
    docs = [h for h in intake.hunks if h.kind == "docs"]
    parts = [
        f"<requirement id=\"{req_id}\">\n{req_text.strip()}\n</requirement>",
        f"<reading_A implemented_by_the_change=\"true\">\n{by.get('A', '')}\n</reading_A>",
        f"<reading_B build_this=\"true\">\n{by.get('B', '')}\n</reading_B>",
        "<files note=\"current content on the change's head, line endings normalised to LF\">",
    ]
    for path, content in context_files(intake, wt):
        parts.append(f"<file path=\"{path}\">\n{content}\n</file>")
    parts.append("</files>")
    for path, excerpt in public_api_excerpts(wt):
        parts.append(f"<public_api_excerpt path=\"{path}\">\n{excerpt}\n</public_api_excerpt>")
    for h in docs:
        parts.append(f"<docs_hunk file=\"{h.file}\">\n{h.text}\n</docs_hunk>")
    return "\n\n".join(parts)


def apply_edits(wt: Path, edits: list[dict]) -> list[str]:
    """Apply search/replace edits; raise EditError naming every edit that did not apply."""
    errors: list[str] = []
    planned: dict[str, str] = {}
    for n, e in enumerate(edits, start=1):
        rel = e["path"].replace("\\", "/").lstrip("/")
        if ".." in rel.split("/") or not rel.startswith(("src/", "tests/", "README.md")):
            errors.append(f"edit {n}: path {rel!r} is outside src/, tests/ and README.md")
            continue
        p = wt / rel
        if not e["search"]:
            if p.exists() or rel in planned:
                errors.append(f"edit {n}: {rel} already exists; use a search/replace edit")
                continue
            planned[rel] = e["replace"].replace("\r\n", "\n").replace("\n", "\r\n")
            continue
        if rel not in planned:
            if not p.exists():
                errors.append(f"edit {n}: {rel} does not exist")
                continue
            planned[rel] = p.read_bytes().decode("utf-8")
        text = planned[rel]
        crlf = "\r\n" in text
        search = e["search"].replace("\r\n", "\n")
        replace = e["replace"].replace("\r\n", "\n")
        if crlf:
            search, replace = search.replace("\n", "\r\n"), replace.replace("\n", "\r\n")
        count = text.count(search)
        if count != 1:
            errors.append(f"edit {n}: search text occurs {count} times in {rel} (must be exactly "
                          f"once); first line: {e['search'].splitlines()[0][:120] if e['search'] else ''!r}")
            continue
        planned[rel] = text.replace(search, replace, 1)
    if errors:
        raise EditError("\n".join(errors))
    for rel, text in planned.items():
        dest = wt / rel
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_bytes(text.encode("utf-8"))
    return sorted(planned)


def _failures(run: TestRun, run_dir: Path, log_rel: str) -> list[str]:
    """Sorted, because the errors go into the retry prompt (the fixture key) and TRX and parallel
    build output order varies between runs."""
    if not run.compiled:
        errors = compile_errors(run_dir, log_rel, limit=500)
        return sorted(errors)[:15] or ["build failed (no compiler error parsed)"]
    out = sorted(f"test failed: {c.name}: {(c.message.splitlines() or [''])[0][:300]}"
                 for c in run.cases if c.outcome == "failed")
    if run.total == 0:
        out.append("no tests ran")
    return out


def _commit(wt: Path, message: str, date_of: str) -> str:
    date = git("show", "-s", "--format=%cI", date_of, cwd=wt).strip()
    env = {**os.environ, "GIT_AUTHOR_NAME": AUTHOR_NAME, "GIT_AUTHOR_EMAIL": AUTHOR_EMAIL,
           "GIT_COMMITTER_NAME": AUTHOR_NAME, "GIT_COMMITTER_EMAIL": AUTHOR_EMAIL,
           "GIT_AUTHOR_DATE": date, "GIT_COMMITTER_DATE": date}
    git("add", "-A", cwd=wt)
    proc = subprocess.run(["git", "commit", "--no-verify", "-q", "-m", message], cwd=str(wt),
                          env=env, capture_output=True, text=True)
    if proc.returncode != 0:
        raise RuntimeError(f"git commit failed: {proc.stderr.strip() or proc.stdout.strip()}")
    return rev_parse(wt, "HEAD")


def _set_branch(name: str, sha: str) -> None:
    git("branch", "-f", name, sha, cwd=config.MOCK_REPO)


def _quote(req_text: str, quote: str) -> str:
    q = " ".join(quote.split()).strip().strip('"')
    return q.rstrip(".") if q and q in " ".join(req_text.split()) else ""


def refusal_for(req_id: str, req_text: str, interps: list[Interpretation], quote: str,
                panel: str = "") -> Refusal:
    """`panel`: the reading panel's vote sentence (lenses.readers); it leads the refusal."""
    by = {i.label: i.summary for i in interps}
    q = _quote(req_text, quote)
    support = (f"the requirement text supports both: \"{q}\"" if q else
               "the requirement text supports both readings and nothing recorded settles it")
    lead = (f"The AI will not pick: {panel.strip()}" if panel
            else f"The AI will not pick, because {support}.")
    return Refusal(
        what=f"Choosing between reading A and reading B of {req_id}",
        why=(f"{lead} Reading A: {by.get('A', '')} "
             f"Reading B: {by.get('B', '')} Which one ships is a product decision, not a "
             "correctness question; both branches are built and reviewed with their own receipts, "
             "and a named human merges one."),
    )


def build_branches(change_id: str, req_id: str, req_text: str, interps: list[Interpretation],
                   intake: IntakeReport, run_dir: Path, commands: list,
                   panel: str = "") -> ABResult:
    res = ABResult()
    head_sha = intake.head_sha
    _set_branch(branch_name(change_id, "A"), head_sha)
    res.branches["A"], res.shas["A"] = branch_name(change_id, "A"), head_sha

    wt = sandbox.worktree("ab", head_sha, name=f"ab-{change_id}")
    quote = ""
    with sandbox.lock(wt):
        git("checkout", "--detach", "--force", head_sha, cwd=wt)
        sandbox.reset(wt)
        user = build_prompt(req_id, req_text, interps, intake, wt)
        prompt = user
        attempts: list[dict] = []
        sha = ""
        for attempt in range(1, MAX_ATTEMPTS + 1):
            try:
                out = llm.call_json("ab-variant", config.MODEL_DEEP, SYSTEM, prompt, SCHEMA,
                                    effort="high", max_tokens=48000)
            except llm.LLMUnavailable as e:
                attempts.append({"attempt": attempt, "error": f"LLM unavailable: {e}"})
                break
            res.cost_usd += llm.cost_usd(config.MODEL_DEEP, out.input_tokens, out.output_tokens)
            res.tokens = {"input": res.tokens.get("input", 0) + out.input_tokens,
                          "output": res.tokens.get("output", 0) + out.output_tokens,
                          "from_fixture": out.from_fixture, "model": config.MODEL_DEEP}
            data = out.data
            quote = quote or data.get("quote_supporting_both", "")
            res.summary_b = data.get("summary", "").strip()
            sandbox.reset(wt)
            rid = f"ab-B-test-{attempt}"
            try:
                changed = apply_edits(wt, data.get("edits") or [])
                if not changed:
                    raise EditError("no edits returned")
                run = sandbox.test(wt, run_dir, rid, "head", commands=commands)
                errors = _failures(run, run_dir, f"logs/{rid}.log")
            except EditError as e:
                changed, run, errors = [], None, [f"edits did not apply: {line}"
                                                  for line in str(e).splitlines()]
            attempts.append({"attempt": attempt, "files": changed, "errors": errors[:20],
                             "test_receipt": rid if run else "",
                             "tests": f"{run.passed}/{run.total} passed" if run else ""})
            if not errors:
                msg = f"Reading B for {req_id}: {res.summary_b or 'alternative reading'}"
                sha = _commit(wt, msg, head_sha)
                break
            prompt = (user + "\n\n<previous_attempt>\n" + json.dumps(data, indent=1, ensure_ascii=False)
                      + "\n</previous_attempt>\n\n<errors note=\"build or test output after applying "
                        "the previous attempt to the original head; fix them in a complete new set "
                        "of edits against the original head\">\n" + "\n".join(errors[:20])
                      + "\n</errors>")
        stat = git("diff", "--stat", head_sha, sha, cwd=wt).strip() if sha else ""
        git("checkout", "--detach", "--force", head_sha, cwd=wt)
        sandbox.reset(wt)

    res.refusal = refusal_for(req_id, req_text, interps, quote, panel)
    receipts = [a["test_receipt"] for a in attempts if a.get("test_receipt")]
    if sha:
        _set_branch(branch_name(change_id, "B"), sha)
        res.branches["B"], res.shas["B"], res.diff_stat = branch_name(change_id, "B"), sha, stat
        res.evidence.append(Evidence(
            id="ab-variant-B", kind="build", result="pass", command_ids=receipts,
            title=(f"Reading B built as {branch_name(change_id, 'B')} ({sha[:8]}): builds and the "
                   f"SourceGenerator tests pass (attempt {len(attempts)} of {MAX_ATTEMPTS})"),
            details={"branch": branch_name(change_id, "B"), "sha": sha, "summary": res.summary_b,
                     "diff": stat, "attempts": attempts, "author": f"{AUTHOR_NAME} <{AUTHOR_EMAIL}>",
                     "cost_usd": round(res.cost_usd, 6)},
            excerpt=stat[-1500:]))
    else:
        res.branches["B"] = ""
        last = attempts[-1] if attempts else {"errors": ["no attempt made"]}
        res.evidence.append(Evidence(
            id="ab-variant-B", kind="build", result="fail", command_ids=receipts,
            title=f"Reading B could not be built as a working branch after {len(attempts)} attempt(s)",
            details={"attempts": attempts}, excerpt="\n".join(last.get("errors", [])[:8])))
        res.not_checked.append(NotChecked(
            what="reading B branch and its receipts",
            why=f"the B variant did not build or pass tests after {len(attempts)} attempt(s): "
                + "; ".join(last.get("errors", [])[:2])))
    return res
