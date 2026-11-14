"""Bench oracle: a simulated human that answers a bench case's intent questions from ground truth.

It exists only for the cold -> warm learning-curve measurement (SPEC 7, T5). Every answer it writes is
labelled `answered_by = ORACLE` in context/ledger.jsonl, and the metrics notes say so.

Policy:
  1. A question that touches one of the bait conventions (bench/bait-conventions.md) is answered with
     that convention's recorded text: the behaviour is deliberate.
     "Touches" = shares words distinctive to that convention (not used by the other conventions).
  2. Otherwise the option the requirement clearly favours (see pick_option): most words shared
     with affirmed requirement clauses, ahead of the runner-up, and not contested by a negated one.
  3. With no clear winner: NO_PREFERENCE.
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from pathlib import Path

from . import config
from .models import LedgerEntry, Question
from .questions import _STOP, normalise

ORACLE = "bench-oracle (simulated human)"
NO_PREFERENCE = "No preference; follow the requirement"
CONVENTIONS_FILE = config.BENCH / "bait-conventions.md"
MIN_DISTINCTIVE = 3  # distinctive, non-requirement convention words to touch it
BAIT_MIN_DISTINCTIVE = 3  # lower bar on the bait case built on that convention
MIN_OPTION_OVERLAP = 2
OPTION_MARGIN = 2

_SECTION_RE = re.compile(r"^## ([A-Z])\. (.+)$", re.M)
_ENTRY_RE = re.compile(r'Ledger entry \(suggested\): "(.+)"\s*$', re.M)
_USED_BY_RE = re.compile(r"`(mock-\d+-[a-z-]+)`")
_CODE_RE = re.compile(r"`([^`]+)`")


@dataclass
class Convention:
    key: str  # "A", "B", "C"
    title: str
    entry: str  # the suggested ledger text: what the oracle answers
    used_by: list[str]  # manifest ids of the bait cases built on it
    words: set[str]  # content words of title + statement + entry, for matching
    anchors: set[str]  # identifiers in backticks and title words: one must be shared to match


def _content_words(text: str) -> set[str]:
    return {w for w in normalise(text).split()
            if w not in _STOP and len(w) > 2 and not w.isdigit()}


def load_conventions(path: Path | None = None) -> list[Convention]:
    text = (path or CONVENTIONS_FILE).read_text(encoding="utf-8")
    matches = list(_SECTION_RE.finditer(text))
    out: list[Convention] = []
    for i, m in enumerate(matches):
        body = text[m.end(): matches[i + 1].start() if i + 1 < len(matches) else len(text)]
        entry = _ENTRY_RE.search(body)
        if not entry:
            continue
        # Match on what the convention says, not on the "Evidence"/"Used by" notes, which name
        # bench cases and unrelated features.
        statement = re.split(r"^(Evidence in code|Used by)", body, maxsplit=1, flags=re.M)[0]
        about = f"{m.group(2)} {statement} {entry.group(1)}"
        idents = " ".join(re.sub(r"([a-z])([A-Z])", r"\1 \2", c) for c in _CODE_RE.findall(about))
        out.append(Convention(
            key=m.group(1), title=m.group(2).strip(), entry=entry.group(1).strip(),
            used_by=sorted(set(_USED_BY_RE.findall(body))),
            words=_content_words(f"{about} {idents}"),
            anchors=(_content_words(m.group(2)) - _requirement_vocabulary())
            | {t.lower() for c in _CODE_RE.findall(about) for t in re.findall(r"[A-Za-z]\w{3,}", c)}))
    return out


def distinctive_words(conv: Convention, conventions: list[Convention]) -> set[str]:
    """Words of this convention that the other conventions do not use. Shared vocabulary
    (generator, analyzer, types, diagnostics) is everywhere in this codebase and proves nothing."""
    others = set().union(*(c.words for c in conventions if c.key != conv.key))
    return conv.words - others


def _requirement_vocabulary() -> set[str]:
    words: set[str] = set()
    for f in config.REQUIREMENTS.glob("REQ-*.md"):
        words |= _content_words(f.read_text(encoding="utf-8"))
    return words


def matching_convention(question: Question, case_id: str,
                        conventions: list[Convention]) -> Convention | None:
    """On the bait case built on a convention: BAIT_MIN_DISTINCTIVE of its distinctive words.
    Elsewhere: MIN_DISTINCTIVE distinctive words that no requirement uses either, so a question
    about the requirement's own subject (formatters, resolvers, referenced assemblies) never
    gets a convention's "deliberate" answer."""
    qwords = _content_words(f"{question.text} {question.why_it_matters}")
    req_vocab = _requirement_vocabulary()
    best, best_shared = None, 0
    for conv in conventions:
        own = case_id in conv.used_by
        vocab = distinctive_words(conv, conventions)
        hits = qwords & (vocab if own else vocab - req_vocab)
        bar = BAIT_MIN_DISTINCTIVE if own else MIN_DISTINCTIVE
        if len(hits) >= bar and qwords & conv.anchors and len(hits) > best_shared:
            best, best_shared = conv, len(hits)
    return best


_NEGATION_RE = re.compile(r"\b(not|no|never|nor|without|don't|doesn't|isn't)\b", re.I)


def _requirement_words(requirement_text: str) -> tuple[set[str], set[str]]:
    """(affirmed words, negated words): in each sentence, words after a negation cue are negated
    ("I do not feel like a setting is needed"), words before it are affirmed."""
    pos: set[str] = set()
    neg: set[str] = set()
    for sentence in re.split(r"(?<=[.!?])\s+|\n+", requirement_text):
        m = _NEGATION_RE.search(sentence)
        if m is None:
            pos |= _content_words(sentence)
        else:
            pos |= _content_words(sentence[:m.start()])
            neg |= _content_words(sentence[m.end():])
    return pos, neg


def _affirmed(text: str) -> set[str]:
    """Content words of an option before any negation cue ("Unconditional, no setting" ->
    {unconditional})."""
    m = _NEGATION_RE.search(text)
    return _content_words(text[:m.start()] if m else text)


def pick_option(options: list[str], requirement_text: str) -> str:
    """The option the requirement clearly favours, else NO_PREFERENCE.

    Score = affirmed option words found in affirmed requirement clauses. The winner must reach
    MIN_OPTION_OVERLAP, lead the runner-up by OPTION_MARGIN, and use no word the requirement
    negates anywhere ("I do not feel like a setting is needed, but a setting would be fine" makes
    any setting option contested). A wrong recorded answer misleads every later review of the
    area, so doubt means no preference."""
    pos, neg = _requirement_words(requirement_text)
    scored = sorted(((len(_affirmed(o) & pos), o) for o in options), key=lambda x: -x[0])
    if not scored or scored[0][0] < MIN_OPTION_OVERLAP:
        return NO_PREFERENCE
    if len(scored) > 1 and scored[0][0] - scored[1][0] < OPTION_MARGIN:
        return NO_PREFERENCE
    if _affirmed(scored[0][1]) & neg:
        return NO_PREFERENCE
    return scored[0][1]


def answer(question: Question, case_id: str, requirement_text: str,
           conventions: list[Convention] | None = None) -> tuple[str, str]:
    """(answer text, basis) for one question. basis is "convention <key>", "requirement" or "none"."""
    conventions = load_conventions() if conventions is None else conventions
    conv = matching_convention(question, case_id, conventions)
    if conv is not None:
        return f"Deliberate, not a defect: {conv.entry}", f"convention {conv.key}"
    choice = pick_option(question.options, requirement_text)
    return choice, "requirement" if choice != NO_PREFERENCE else "none"


def answer_questions(questions: list[Question], case_id: str, requirement_text: str,
                     ledger: list[dict], append) -> list[dict]:
    """Answer every question of one case not already in the ledger (same area + same text).

    `append(LedgerEntry)` writes the entry (context.append_ledger in production). Returns a record per
    answered question for the bench summary.
    """
    from .questions import same_question

    conventions = load_conventions()
    answered = [(e.get("area", ""), e.get("question", "")) for e in ledger]
    out = []
    for q in questions:
        if any(a == q.area and same_question(t, q.text) for a, t in answered):
            continue
        text, basis = answer(q, case_id, requirement_text, conventions)
        append(LedgerEntry(question=q.text, answer=text, area=q.area, answered_by=ORACLE,
                           change_id=case_id, question_id=q.id))
        answered.append((q.area, q.text))
        out.append({"question_id": q.id, "area": q.area, "basis": basis, "answer": text})
    return out
