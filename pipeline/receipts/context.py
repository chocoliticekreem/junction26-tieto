"""Review context ledger: bootstrap, area-scoped retrieval, decision log (SPEC 5.6).

context/REVIEW_CONTEXT.md is a markdown file of bullets, each tagged with an area and a provenance:
    - [area: analyzer] text (source: doc/analyzers/MsgPack004.md)
Retrieval is by area tag only (no repo indexing per review). Answered questions live in
context/ledger.jsonl and are summarised under "Decisions (from answered questions)".
"""

import json
import re
import subprocess
from dataclasses import dataclass
from datetime import datetime

from . import config, llm
from .models import ContextEntryUsed, LedgerEntry

SECTIONS = ["Product", "Users", "Public contract", "Deliberate conventions", "Risk areas"]
DECISIONS_HEADING = "Decisions (from answered questions)"
NO_DECISIONS = "_No decisions recorded yet._"
AREAS = ["product", "annotations", "serializer", "generator", "analyzer", "security", "build", "docs"]
# Intake areas use code paths (intake.AREA_TABLE); context bullets use product tags (AREAS).
# TypeCollector serves both the generator and the analyzers.
AREA_TAGS = {
    "type-collector": ("generator", "analyzer"),
    "codegen-templates": ("generator",),
    "source-generator": ("generator",),
    "analyzer": ("analyzer",),
    "public-api": ("annotations",),
    "serializer-format": ("serializer",),
    "serializer-runtime": ("serializer",),
    "security": ("security",),
    "docs": ("docs",),
}
SEED_MARKER ="<!-- seeded convention for the bait bench case -->"
README_FOCUS = ["Analyzer", "Security", "Object Serialization"]
GIT_LOG_LIMIT = 120

# Human-seeded (SPEC 5.6 "seeded bait"). Verified against base 32258fdb TypeCollector.cs:367-370.
SEEDED_CONVENTIONS = [
    (
        ["analyzer", "generator"],
        "Types that come from a referenced assembly are deliberately not analysed and get no formatter "
        "from the compiling assembly. In `TypeCollector.CollectCore`, after the custom-formatter, enum "
        "and generic checks, a non-generic type whose symbol is metadata-only exits with a bare "
        "`if (type.Locations[0].IsInMetadata) { return; }` (TypeCollector.cs:367-370), so no "
        "MsgPack003/004/005 diagnostic and no generated formatter appear for it. The defining assembly "
        "owns validating and formatting its own types (rationale inferred: the code has no comment). "
        "A missing diagnostic or formatter for such a type is not a defect.",
        "src/MessagePack.SourceGenerator/CodeAnalysis/TypeCollector.cs:367",
    ),
]


@dataclass
class Entry:
    section: str
    area: list[str]
    text: str
    source: str


# Source gathering -----------------------------------------------------------


def _git(*args: str, check: bool = True) -> str:
    r = subprocess.run(["git", "-C", str(config.SAMPLE_CLONE), *args], capture_output=True, text=True)
    if r.returncode != 0:
        if check:
            raise RuntimeError(f"git {' '.join(args)} failed: {r.stderr.strip()}")
        return ""
    return r.stdout


def _show(path: str) -> str:
    return _git("show", f"{config.BASE_SHA}:{path}", check=False)


def _ls(directory: str) -> list[str]:
    out = _git("ls-tree", "--name-only", config.BASE_SHA, directory.rstrip("/") + "/", check=False)
    return [line for line in out.splitlines() if line]


def _since_12_months() -> str:
    base = datetime.fromisoformat(_git("log", "-1", "--format=%cI", config.BASE_SHA).strip())
    try:
        return base.replace(year=base.year - 1).isoformat()
    except ValueError:
        return base.replace(year=base.year - 1, day=28).isoformat()


def _git_summaries() -> list[str]:
    out = _git("log", config.BASE_SHA, "--no-merges", f"--since={_since_12_months()}", "--format=%s")
    lines = [s for s in out.splitlines() if s and not s.startswith("Bump ")]
    return lines[:GIT_LOG_LIMIT]


def gather_sources() -> dict[str, str]:
    """Source name -> text, from the sample repo at the base commit. Seeds first, then docs."""
    src: dict[str, str] = {}
    for seed in ("CLAUDE.md", "AGENTS.md"):
        if text := _show(seed):
            src[seed] = text
    for path in ("README.md", "CONTRIBUTING.md"):
        src[path] = _show(path)
    for path in _ls("doc/analyzers"):
        if path.endswith(".md"):
            src[path] = _show(path)
    for path in _ls("src/MessagePack.SourceGenerator"):
        if "/AnalyzerReleases." in path and path.endswith(".md"):
            src[path] = _show(path)
    src["git log"] = "\n".join(_git_summaries())
    return {k: v for k, v in src.items() if v.strip()}


# Text helpers ---------------------------------------------------------------


def _readme_section(text: str, title: str) -> str:
    m = re.search(rf"^##+ {re.escape(title)}\b.*?$(.*?)(?=^## |\Z)", text, re.M | re.S)
    return m.group(1).strip() if m else ""


def _sentences(text: str, n: int = 2) -> list[str]:
    text = re.sub(r"```.*?```", " ", text, flags=re.S)
    text = re.sub(r"!\[[^\]]*\]\([^)]*\)", "", text)
    text = re.sub(r"\[\s*\]\([^)]*\)", "", text)
    text = re.sub(r"\[([^\]]*)\]\([^)]*\)", r"\1", text)
    text = re.sub(r"^#+\s*", "", text, flags=re.M)
    text = re.sub(r"[>*`]", "", text)
    text = re.sub(r"\s+", " ", text).strip()
    return [s.strip() for s in re.split(r"(?<=[.!?])\s+(?=[A-Z])", text) if len(s.strip()) > 20][:n]


def _doc_summary(name: str, text: str) -> str:
    title = text.splitlines()[0].lstrip("# ").strip() if text else name
    title = re.sub(r"^MsgPack\d{3}\s+", "", title)
    body = "\n".join(text.splitlines()[1:])
    first = _sentences(body, 1)
    return f"{title}: {first[0]}" if first else title


def _clip(text: str, limit: int) -> str:
    return text if len(text) <= limit else text[:limit] + "\n[truncated]"


# Summarisation --------------------------------------------------------------

SYSTEM = (
    "You write the review context for an AI code reviewer. You are given documents from one repository. "
    "Produce short factual bullets only from those documents. Every bullet needs the source document "
    "it came from, copied exactly from the document headers. Never invent facts, rules or file names. "
    "Prefer rules a reviewer could wrongly flag or wrongly miss: public contract, conventions, risky areas."
)


def _user_prompt(sources: dict[str, str]) -> str:
    parts = [
        "Sections: Product (what it is), Users (who uses it and how), Public contract (analyzer rules, "
        "attributes, serialization format promises), Deliberate conventions (things that look like bugs "
        "but are intended), Risk areas (where changes are dangerous, from docs and recent history). "
        f"Areas allowed: {', '.join(AREAS)}. 2 to 8 bullets per section, one sentence each.",
    ]
    for name, text in sources.items():
        if name == "README.md":
            body = _clip(text, 10000) + "".join(
                f"\n\n[README section: {t}]\n{_clip(_readme_section(text, t), 5000)}" for t in README_FOCUS
            )
        elif name.startswith("doc/analyzers/"):
            body = _doc_summary(name, text)
        else:
            body = _clip(text, 6000)
        parts.append(f"=== SOURCE: {name} ===\n{body}")
    return "\n\n".join(parts)


def _schema() -> dict:
    return {
        "type": "object",
        "additionalProperties": False,
        "required": ["entries"],
        "properties": {
            "entries": {
                "type": "array",
                "items": {
                    "type": "object",
                    "additionalProperties": False,
                    "required": ["section", "area", "text", "source"],
                    "properties": {
                        "section": {"type": "string", "enum": SECTIONS},
                        "area": {"type": "string", "enum": AREAS},
                        "text": {"type": "string"},
                        "source": {"type": "string"},
                    },
                },
            }
        },
    }


def llm_entries(sources: dict[str, str]) -> list[Entry]:
    """Raises llm.LLMUnavailable when there is no key and no fixture."""
    res = llm.call_json("context-bootstrap", config.MODEL_LENS, SYSTEM, _user_prompt(sources), _schema())
    known = set(sources)
    entries = []
    for e in res.data.get("entries", []):
        if e["section"] in SECTIONS and e["source"] in known and e["text"].strip():
            entries.append(Entry(e["section"], [e["area"]], e["text"].strip(), e["source"]))
    if not entries:
        raise llm.LLMUnavailable("context-bootstrap returned no entries with a known source")
    return entries


def _rule_rows(text: str) -> list[tuple[str, str, str]]:
    rows = []
    for line in text.splitlines():
        cells = [c.strip() for c in line.split("|")]
        if len(cells) >= 4 and re.fullmatch(r"MsgPack\d{3}", cells[0]):
            rows.append((cells[0], cells[2], cells[3]))
    return rows


def extractive_entries(sources: dict[str, str]) -> list[Entry]:
    """Deterministic offline summary: headings, first sentences, the analyzer rule table."""
    out: list[Entry] = []
    readme = sources.get("README.md", "")
    lead = re.split(r"^## ", readme, maxsplit=1, flags=re.M)[0]
    for s in _sentences(re.sub(r"^#.*$", "", lead, flags=re.M), 3):
        out.append(Entry("Product", ["product"], s, "README.md"))
    for title, area in (("Installation", "product"), ("Unity support", "build"), ("AOT", "generator")):
        for s in _sentences(_readme_section(readme, title), 1):
            out.append(Entry("Users", [area], s, "README.md"))
    if "CONTRIBUTING.md" in sources:
        for s in _sentences(_readme_section(sources["CONTRIBUTING.md"], "How to Build"), 1):
            out.append(Entry("Users", ["build"], s, "CONTRIBUTING.md"))

    releases = "\n".join(v for k, v in sources.items() if "AnalyzerReleases." in k)
    release_src = next((k for k in sources if "AnalyzerReleases.Unshipped" in k), "AnalyzerReleases")
    error_ids: list[str] = []
    for rid, sev, note in _rule_rows(releases):
        doc = f"doc/analyzers/{rid}.md"
        text = f"{rid} ({sev.lower()}): {note}"
        if doc in sources:
            text = f"{rid} ({sev.lower()}): {_doc_summary(doc, sources[doc])}"
        out.append(Entry("Public contract", ["analyzer"], text, doc if doc in sources else release_src))
        if sev.lower() == "error":
            error_ids.append(rid)
    if error_ids:
        out.append(Entry("Risk areas", ["analyzer"], f"{len(error_ids)} analyzer rules are Error severity "
                         f"({', '.join(error_ids)}); weakening or removing one can break consumers' builds "
                         "or let invalid contracts through.", release_src))
    for title, area in (("Object Serialization", "serializer"), ("Security", "security")):
        for s in _sentences(_readme_section(readme, title), 2):
            out.append(Entry("Public contract", [area], s, "README.md"))
    for s in _sentences(_readme_section(readme, "Analyzer"), 40):
        if re.search(r"\b(only|must|not|never|always|by default)\b", s):
            out.append(Entry("Deliberate conventions", ["analyzer"], s, "README.md"))
            if len([e for e in out if e.section == "Deliberate conventions"]) >= 4:
                break
    risky = re.compile(r"crash|security|trusted|aot|regress|exception|overflow|simd|serializ", re.I)
    for subject in sources.get("git log", "").splitlines():
        if risky.search(subject) and len([e for e in out if e.source == "git log"]) < 6:
            out.append(Entry("Risk areas", ["serializer"], f"Recent change: {subject}", "git log"))
    return out


def seeded_entries() -> list[Entry]:
    return [Entry("Deliberate conventions", areas, text, src) for areas, text, src in SEEDED_CONVENTIONS]


# Rendering and parsing ------------------------------------------------------


def _bullet(e: Entry) -> str:
    line = f"- [area: {', '.join(e.area)}] {e.text} (source: {e.source})"
    seeded = any(e.text == t and e.source == s for _, t, s in SEEDED_CONVENTIONS)
    return f"{line} {SEED_MARKER}" if seeded else line


def render(entries: list[Entry], mode: str) -> str:
    lines = [
        "# Review context",
        "",
        f"Status: draft (not confirmed by a named human). Built from the sample repo at {config.BASE_SHA} "
        f"by {mode}.",
        "",
    ]
    for section in SECTIONS:
        lines += [f"## {section}", ""]
        lines += [_bullet(e) for e in entries if e.section == section] or ["_Nothing recorded._"]
        lines.append("")
    lines += [f"## {DECISIONS_HEADING}", "", NO_DECISIONS, ""]
    return "\n".join(lines)


_BULLET_RE = re.compile(r"^- \[area: ([^\]]+)\] (.*)$")


def _tokens(area: str) -> set[str]:
    return {t for t in re.split(r"[/\-_\s]+", area.lower()) if t}


def _matches(tag: str, requested: list[str]) -> bool:
    want = _tokens(tag)
    return any(want <= _tokens(r) for r in requested)


def _strip_markers(line: str) -> str:
    return re.sub(r"\s*<!--.*?-->", "", line).strip()


def entries_for_areas(areas: list[str]) -> list[str]:
    """Bullets tagged with any of `areas` (tag tokens must all appear in the requested area), plus
    ledger answers for those areas. Nothing else is returned: no full-context dump per review."""
    out: list[str] = []
    path = config.CONTEXT / "REVIEW_CONTEXT.md"
    if path.exists():
        in_decisions = False
        for line in path.read_text().splitlines():
            if line.startswith("## "):
                in_decisions = line[3:].startswith("Decisions")
                continue
            m = _BULLET_RE.match(line)
            if m and not in_decisions and _matches_any_tag(m.group(1), areas):
                out.append(_strip_markers(line))
    ledger = config.CONTEXT / "ledger.jsonl"
    if ledger.exists():
        for raw in ledger.read_text().splitlines():
            if not raw.strip():
                continue
            e = LedgerEntry.model_validate_json(raw)
            if _matches(e.area, areas):
                out.append(f"- [area: {e.area}] Q: {e.question} A: {e.answer} "
                           f"(source: ledger, answered by {e.answered_by})")
    return out


def is_confirmed() -> bool:
    """True only after a named human confirmed the context in the dashboard (status.json)."""
    path = config.CONTEXT / "status.json"
    try:
        return json.loads(path.read_text()).get("status") == "confirmed"
    except (OSError, ValueError, AttributeError):
        return False


def confirmed_by() -> str | None:
    """The named human in status.json when the context is confirmed, else None."""
    if not is_confirmed():
        return None
    return json.loads((config.CONTEXT / "status.json").read_text()).get("confirmed_by")


_SOURCE_RE = re.compile(r"^(.*) \(source: ([^()]*)\)$", re.S)


def used_entry(e: dict) -> ContextEntryUsed:
    """One review_ledger item as the dashboard shows it: plain text, area, provenance."""
    if e.get("question"):
        return ContextEntryUsed(text=f"Q: {e['question']} A: {e['answer']}", area=e["area"],
                                source=f"ledger: answered by {e['answered_by']}", kind="ledger")
    m = _SOURCE_RE.match(_strip_markers(e["text"]))
    text, source = (m.group(1), m.group(2)) if m else (_strip_markers(e["text"]), "")
    return ContextEntryUsed(text=text, area=e["area"], source=source, kind="context")


def context_tags(areas: list[str]) -> list[str]:
    """Intake areas (intake.AREA_TABLE) plus the REVIEW_CONTEXT.md tags (AREAS) they belong to."""
    return sorted({*areas, *(t for a in areas for t in AREA_TAGS.get(a, ()))})


def review_ledger(areas: list[str]) -> list[dict]:
    """What a lens sees for intake `areas`: the context bullets tagged with those areas or their
    context tags as {"area", "text"}, plus the matching ledger.jsonl answers as LedgerEntry dicts
    (question + area, so the question budget can tell an already-answered question)."""
    out: list[dict] = []
    for line in entries_for_areas(context_tags(areas)):
        m = _BULLET_RE.match(line)
        if m and "(source: ledger," not in m.group(2):
            out.append({"area": m.group(1), "text": m.group(2)})
    ledger = config.CONTEXT / "ledger.jsonl"
    if ledger.exists():
        for raw in ledger.read_text().splitlines():
            if raw.strip():
                e = LedgerEntry.model_validate_json(raw)
                if _matches(e.area, areas):
                    out.append(e.model_dump())
    return out


def _matches_any_tag(tag_field: str, areas: list[str]) -> bool:
    return any(_matches(t.strip(), areas) for t in tag_field.split(","))


def append_ledger(entry: LedgerEntry) -> None:
    config.CONTEXT.mkdir(parents=True, exist_ok=True)
    with (config.CONTEXT / "ledger.jsonl").open("a") as f:
        f.write(entry.model_dump_json() + "\n")
    md = config.CONTEXT / "REVIEW_CONTEXT.md"
    if not md.exists():
        return
    text = md.read_text()
    summary = (f"- [area: {entry.area}] Q: {entry.question} A: {entry.answer} "
               f"(source: ledger, answered by {entry.answered_by}, {entry.ts[:10]})")
    heading = f"## {DECISIONS_HEADING}"
    if heading not in text:
        text = text.rstrip("\n") + f"\n\n{heading}\n\n"
    text = text.replace(NO_DECISIONS + "\n", "")
    md.write_text(text.rstrip("\n") + "\n" + summary + "\n")


# Entry point ----------------------------------------------------------------


def bootstrap() -> int:
    status_path = config.CONTEXT / "status.json"
    if status_path.exists() and json.loads(status_path.read_text()).get("status") != "draft":
        print("context/status.json is not draft; refusing to overwrite a confirmed context")
        return 1
    sources = gather_sources()
    print(f"sources: {len(sources)} ({', '.join(list(sources)[:4])}, ...)")
    try:
        entries = llm_entries(sources)
        mode = f"{config.MODEL_LENS} (purpose context-bootstrap)"
    except llm.LLMUnavailable as e:
        print(f"LLM unavailable ({e}); using deterministic extractive summary")
        entries = extractive_entries(sources)
        mode = "the deterministic extractive fallback"
    entries += seeded_entries()
    config.CONTEXT.mkdir(parents=True, exist_ok=True)
    (config.CONTEXT / "REVIEW_CONTEXT.md").write_text(render(entries, mode))
    status_path.write_text(json.dumps({"status": "draft", "confirmed_by": None, "ts": None}, indent=2) + "\n")
    print(f"wrote {config.CONTEXT / 'REVIEW_CONTEXT.md'} with {len(entries)} entries ({mode})")
    return 0
