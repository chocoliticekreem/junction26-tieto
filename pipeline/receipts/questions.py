"""[6] Intent-question budget (SPEC 5.6, principle 5).

select_questions keeps at most config.QUESTION_BUDGET questions per change, only those whose
verdict depends on intent, and never re-asks a question already answered in a matching area
(context/ledger.jsonl entries) or already chosen earlier in the same run.

Same question = Jaccard similarity >= SIMILARITY over stemmed content words (stopwords removed;
plural, -ing and -ed endings stripped), so a reworded question still matches. Matching area = the
same intake area, or two areas that map to a shared context tag (context.AREA_TAGS), e.g.
`analyzer` and `type-collector` both feed the analyzers. Deterministic: no model call.
"""

import json
import re
from pathlib import Path

from pydantic import BaseModel

from . import config
from .context import AREA_TAGS
from .models import Question

_STOP = {
    "a", "an", "the", "is", "are", "be", "been", "being", "was", "were", "should", "would",
    "could", "does", "do", "did", "it", "its", "this", "that", "these", "those", "to", "of",
    "for", "from", "in", "into", "on", "or", "and", "we", "you", "our", "by", "with", "as", "at",
    "can", "will", "there", "their", "than", "then", "also", "when", "which", "what", "if", "any",
}
SIMILARITY = 0.6


def normalise(text: str) -> str:
    return " ".join(re.sub(r"[^a-z0-9 ]+", " ", text.lower()).split())


def stem(word: str) -> str:
    """Light, deterministic suffix stripping: enough to equate report/reported/reports."""
    if len(word) > 4 and word.endswith("ies"):
        word = word[:-3] + "y"
    elif len(word) > 5 and word.endswith("ing"):
        word = word[:-3]
    elif len(word) > 4 and word.endswith("ed"):
        word = word[:-2]
    elif len(word) > 4 and word.endswith("es") and word[-4:-2] in ("ss", "sh", "ch", "xe"):
        word = word[:-2]
    elif len(word) > 3 and word.endswith("s") and not word.endswith("ss"):
        word = word[:-1]
    if len(word) > 4 and word.endswith("e"):
        word = word[:-1]
    return word


def _words(text: str) -> set[str]:
    return {stem(w) for w in normalise(text).split() if w not in _STOP}


def same_area(a: str, b: str) -> bool:
    return a == b or bool({a, *AREA_TAGS.get(a, ())} & {b, *AREA_TAGS.get(b, ())})


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
        if any(same_area(area, c.area) and same_question(q, c.text) for area, q in answered):
            continue
        if any(same_area(q.area, c.area) and same_question(q.text, c.text) for q in chosen):
            continue
        chosen.append(Question.model_validate(c.model_dump(include=set(Question.model_fields))))
    return chosen
