"""Deterministic C# mutant generation on the added lines of a hunk (SPEC 5.5, no LLM).

Operators, each applied to the first match on a line:
  negate-condition  `if (c)` -> `if (!(c))`; else `is null` <-> `is not null`; else `==` <-> `!=`
  remove-guard      `if (c)` whose body exits (return/throw/continue/break) -> `if (false)`;
                    any other `if (c)` -> `if (true)` (the guarded block always runs)
  off-by-one        `<=` -> `<`, `<` -> `<=`, `>=` -> `>`, `>` -> `>=`, `+ 1` <-> `- 1`,
                    `[0]` <-> `[1]`
  swap-boolean      `true` <-> `false`
  delete-statement  a complete single-line statement is blanked (line numbers stay stable)

Selection per hunk: walk the added lines top to bottom, collecting candidates in operator order
(an operator on a line whose text already appeared in the hunk is skipped); take the first
candidate of each operator, then fill up to the cap in walk order.
"""

import re
from dataclasses import dataclass

OPERATORS = ["negate-condition", "remove-guard", "off-by-one", "swap-boolean", "delete-statement"]
_EXITS = ("return", "throw", "continue;", "break;")
_SKIP_PREFIXES = ("//", "/*", "*", "using ", "namespace ", "#", "[")
_DECL_PREFIXES = ("public ", "private ", "protected ", "internal ", "static ", "readonly ",
                  "const ", "abstract ", "override ", "virtual ", "sealed ", "partial ")
_CONTINUATION_PREFIXES = (":", "?", ".", "+", "-", "&&", "||", ")", ",")


@dataclass(frozen=True)
class MutantSpec:
    file: str
    line: int  # 1-based line number in the head file
    operator: str
    original: str
    mutated: str


def added_lines(hunk_text: str, new_start: int) -> list[tuple[int, str]]:
    """(head line number, text) of each '+' line in a unified diff hunk body."""
    out: list[tuple[int, str]] = []
    n = new_start
    for raw in hunk_text.split("\n"):
        if raw.startswith("+"):
            out.append((n, raw[1:]))
            n += 1
        elif raw.startswith("-") or raw.startswith("\\"):
            continue
        else:
            n += 1
    return out


def _is_code(line: str) -> bool:
    s = line.strip()
    return bool(s) and s not in ("{", "}", "};", "});") and not s.startswith(_SKIP_PREFIXES)


def _if_condition(line: str) -> tuple[int, int] | None:
    """Span (start, end) of the condition inside the line's `if (...)`, by paren matching."""
    m = re.search(r"\bif\s*\(", line)
    if not m:
        return None
    depth = 0
    for i in range(m.end() - 1, len(line)):
        if line[i] == "(":
            depth += 1
        elif line[i] == ")":
            depth -= 1
            if depth == 0:
                return m.end(), i
    return None  # condition spans several lines


def _body_exits(lines: list[str], idx: int, cond_end: int) -> bool:
    """Whether the statement guarded by the `if` on lines[idx] is an exit."""
    rest = lines[idx][cond_end + 1:].strip()
    if rest and rest != "{":
        return rest.startswith(_EXITS)
    j = idx + 1
    while j < len(lines):
        s = lines[j].strip()
        j += 1
        if not s or s == "{" or s.startswith("//"):
            continue
        return s.startswith(_EXITS)
    return False


def _negate(line: str) -> str | None:
    span = _if_condition(line)
    if span:
        a, b = span
        return f"{line[:a]}!({line[a:b]}){line[b:]}"
    if " is not null" in line:
        return line.replace(" is not null", " is null", 1)
    if " is null" in line:
        return line.replace(" is null", " is not null", 1)
    for x, y in ((" == ", " != "), (" != ", " == ")):
        if x in line:
            return line.replace(x, y, 1)
    return None


def _remove_guard(lines: list[str], idx: int) -> str | None:
    line = lines[idx]
    span = _if_condition(line)
    if not span:
        return None
    a, b = span
    literal = "false" if _body_exits(lines, idx, b) else "true"
    if line[a:b].strip() == literal:
        return None
    return f"{line[:a]}{literal}{line[b:]}"


_OBO = [
    (re.compile(r"(?<=\s)<=(?=\s)"), "<"),
    (re.compile(r"(?<=\s)>=(?=\s)"), ">"),
    (re.compile(r"(?<=\s)<(?=\s)"), "<="),
    (re.compile(r"(?<=\s)>(?=\s)"), ">="),
    (re.compile(r"\+ 1\b"), "- 1"),
    (re.compile(r"- 1\b"), "+ 1"),
    (re.compile(r"\[0\]"), "[1]"),
    (re.compile(r"\[1\]"), "[0]"),
]


def _off_by_one(line: str) -> str | None:
    for rx, repl in _OBO:
        if rx.search(line):
            return rx.sub(repl, line, count=1)
    return None


def _swap_boolean(line: str) -> str | None:
    m = re.search(r"\b(true|false)\b", line)
    if not m:
        return None
    flip = "false" if m.group(1) == "true" else "true"
    return f"{line[:m.start()]}{flip}{line[m.end():]}"


def _delete_statement(lines: list[str], idx: int) -> str | None:
    s = lines[idx].strip()
    if not s.endswith(";") or s.startswith(_DECL_PREFIXES + _CONTINUATION_PREFIXES):
        return None
    if re.match(r"^(return|throw|break|continue|yield|var |using\b)", s):
        return None
    if re.match(r"^[\w<>\[\],.?]+\s+\w+\s*=", s):  # typed local declaration
        return None
    j = idx - 1
    while j >= 0 and not lines[j].strip():
        j -= 1
    prev = lines[j].strip() if j >= 0 else ""
    if prev and not (prev.endswith((";", "{", "}")) or prev.startswith("//")):
        return None  # continuation of a multi-line statement
    return ""


def candidates(lines: list[str], idx: int) -> list[tuple[str, str]]:
    """(operator, mutated line) for one line of a file, in operator order."""
    line = lines[idx]
    if not _is_code(line):
        return []
    out: list[tuple[str, str]] = []
    for op in OPERATORS:
        if op == "negate-condition":
            mut = _negate(line)
        elif op == "remove-guard":
            mut = _remove_guard(lines, idx)
        elif op == "off-by-one":
            mut = _off_by_one(line)
        elif op == "swap-boolean":
            mut = _swap_boolean(line)
        else:
            mut = _delete_statement(lines, idx)
        if mut is not None and mut != line:
            out.append((op, mut))
    return out


def generate(file: str, file_lines: list[str], hunk_text: str, new_start: int,
             cap: int) -> list[MutantSpec]:
    """Up to `cap` mutants on the hunk's added lines. Lines that do not match the head file
    (stale diff) are skipped."""
    pool: list[MutantSpec] = []
    seen: set[tuple[str, str]] = set()
    for n, text in added_lines(hunk_text, new_start):
        idx = n - 1
        if idx >= len(file_lines) or file_lines[idx] != text:
            continue
        for op, mut in candidates(file_lines, idx):
            if (op, text.strip()) in seen:
                continue  # a repeated line (e.g. descriptor boilerplate) tells nothing new
            seen.add((op, text.strip()))
            pool.append(MutantSpec(file=file, line=n, operator=op, original=text, mutated=mut))
    chosen: list[MutantSpec] = []
    for op in OPERATORS:
        first = next((m for m in pool if m.operator == op), None)
        if first is not None and len(chosen) < cap:
            chosen.append(first)
    for m in pool:
        if len(chosen) >= cap:
            break
        if m not in chosen:
            chosen.append(m)
    return sorted(chosen, key=lambda m: (m.line, OPERATORS.index(m.operator)))
