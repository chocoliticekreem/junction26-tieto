"""[6] Intent-question budget (SPEC 5.6, principle 5).

select_questions keeps at most config.QUESTION_BUDGET questions per change, only those whose
verdict depends on intent, and never re-asks a question already answered in the same area
(normalised text match, or near-identical wording, against context/ledger.jsonl entries).
"""

import json
import re
from pathlib import Path

from pydantic import BaseModel

from . import config
from .models import Question

_STOP = {
    "a", "an", "the", "is", "are", "be", "should", "does", "do", "it", "this", "that", "to", "of",
    "for", "in", "on", "or", "and", "we", "you", "our", "by", "with", "as", "at", "can", "will",
}
SIMILARITY = 0.8  # Jaccard over content words; catches re-worded duplicates of the same question


def normalise(text: str) -> str:
    return " ".join(re.sub(r"[^a-z0-9 ]+", " ", text.lower()).split())


def _words(text: str) -> set[str]:
    return {w for w in normalise(text).split() if w not in _STOP}


def same_question(a: str, b: str) -> bool:
    if normalise(a) == normalise(b):
        return True
    wa, wb = _words(a), _words(b)
    if not wa or not wb:
        return False
    return len(wa & wb) / len(wa | wb) >= SIMILARITY


def _field(entry, name: str) -> str:
    d = entry.model_dump() if isinstance(entry, BaseModel) else entry
    return str(d.get(name, "") or "")


def load_ledger(path: Path | None = None) -> list[dict]:
    path = path or config.CONTEXT / "ledger.jsonl"
    if not path.exists():
        return []
    out = []
    for line in path.read_text().splitlines():
        line = line.strip()
        if line:
            out.append(json.loads(line))
    return out


def select_questions(candidates: list, ledger: list, budget: int | None = None) -> list[Question]:
    """Filter lens-proposed questions down to the ones worth a human's time.

    `candidates`: Question-like objects with `verdict_depends_on_intent` (lenses.base.CandidateQuestion).
    `ledger`: LedgerEntry objects or dicts with "question" and "area".
    """
    budget = config.QUESTION_BUDGET if budget is None else budget
    answered = [(_field(e, "area"), _field(e, "question")) for e in ledger if _field(e, "question")]
    chosen: list[Question] = []
    for c in candidates:
        if len(chosen) >= budget:
            break
        if not getattr(c, "verdict_depends_on_intent", False):
            continue
        if any(area == c.area and same_question(q, c.text) for area, q in answered):
            continue
        if any(q.area == c.area and same_question(q.text, c.text) for q in chosen):
            continue
        chosen.append(Question.model_validate(c.model_dump(include=set(Question.model_fields))))
    return chosen
