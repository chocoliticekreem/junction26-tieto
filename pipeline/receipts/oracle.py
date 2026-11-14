"""Bench oracle: a simulated human that answers a bench case's intent questions from ground truth.

It exists only for the cold -> warm learning-curve measurement (SPEC 7, T5). Every answer it writes is
labelled `answered_by = ORACLE` in context/ledger.jsonl, and the metrics notes say so.

Policy:
  1. A question that touches one of the bait conventions (bench/bait-conventions.md) is answered with
     that convention's recorded text: the behaviour is deliberate.
  2. Otherwise the option most consistent with the requirement text (most shared content words) wins.
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
TOUCH_THRESHOLD = 0.25  # share of the question's content words found in the convention
BAIT_TOUCH_THRESHOLD = 0.12  # lower bar on the bait case whose convention it is
MIN_SHARED_WORDS = 3  # short questions must still share real vocabulary with the convention
MIN_OPTION_OVERLAP = 2

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
    words: set[str]  # content words of title + body, for matching


def _content_words(text: str) -> set[str]:
    return {w for w in normalise(text).split() if w not in _STOP and len(w) > 2}


def load_conventions(path: Path | None = None) -> list[Convention]:
    text = (path or CONVENTIONS_FILE).read_text(encoding="utf-8")
    matches = list(_SECTION_RE.finditer(text))
    out: list[Convention] = []
    for i, m in enumerate(matches):
        body = text[m.end(): matches[i + 1].start() if i + 1 < len(matches) else len(text)]
        entry = _ENTRY_RE.search(body)
        if not entry:
            continue
        # Identifiers in backticks (e.g. SuppressSourceGeneration) count as words too.
        idents = " ".join(re.sub(r"([a-z])([A-Z])", r"\1 \2", c) for c in _CODE_RE.findall(body))
        out.append(Convention(
            key=m.group(1), title=m.group(2).strip(), entry=entry.group(1).strip(),
            used_by=sorted(set(_USED_BY_RE.findall(body))),
            words=_content_words(f"{m.group(2)} {body} {idents}")))
    return out


def _touch_score(question: Question, conv: Convention) -> float:
    qwords = _content_words(f"{question.text} {question.why_it_matters}")
    shared = qwords & conv.words
    if len(shared) < MIN_SHARED_WORDS:
        return 0.0
    return len(shared) / len(qwords)


def matching_convention(question: Question, case_id: str,
                        conventions: list[Convention]) -> Convention | None:
    best, best_score = None, 0.0
    for conv in conventions:
        score = _touch_score(question, conv)
        bar = BAIT_TOUCH_THRESHOLD if case_id in conv.used_by else TOUCH_THRESHOLD
        if score >= bar and score > best_score:
            best, best_score = conv, score
    return best


def pick_option(options: list[str], requirement_text: str) -> str:
    req = _content_words(requirement_text)
    scored = sorted(((len(_content_words(o) & req), o) for o in options), key=lambda x: -x[0])
    if not scored or scored[0][0] < MIN_OPTION_OVERLAP:
        return NO_PREFERENCE
    if len(scored) > 1 and scored[1][0] == scored[0][0]:
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
