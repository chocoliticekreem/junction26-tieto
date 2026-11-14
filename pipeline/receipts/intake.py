"""[1] Intake: diff base..head in the bare mock repo, split into files and hunks, classify each file.

Classification (SPEC 5.1), first match wins:
  generated  *.g.cs, *.verified.*, or any path segment containing "snapshot" (case-insensitive)
  test       tests/**
  docs       *.md, doc/**
  template   *.tt (the T4 source that produces generated code)
  source     everything else

On ju26-issue-2297 the 222 generated files are the source-generator snapshot outputs under
tests/MessagePack.SourceGenerator.Tests/Resources/<case>/*.g.cs. The T4 output classes
(src/.../Transforms/*Template.cs) are hand-maintained in the repo and stay `source`.
"""

import re
from pathlib import Path

from . import config
from .gitutil import git, rev_parse
from .models import FileChange, FileKind, Hunk, IntakeReport

# (path prefix, area). Longest matching prefix wins; checked after the kind-based areas.
AREA_TABLE: list[tuple[str, str]] = [
    ("src/MessagePack.SourceGenerator/Analyzers/", "analyzer"),
    ("src/MessagePack.SourceGenerator/CodeAnalysis/", "type-collector"),
    ("src/MessagePack.SourceGenerator/Transforms/", "codegen-templates"),
    ("src/MessagePack.SourceGenerator/", "source-generator"),
    ("src/MessagePack.Analyzers.CodeFixes/", "analyzer"),
    ("src/MessagePack.Annotations/", "public-api"),
    ("src/MessagePack/MessagePackSecurity", "security"),
    ("src/MessagePack/Formatters/", "serializer-format"),
    ("src/MessagePack/Resolvers/", "serializer-format"),
    ("src/MessagePack/", "serializer-runtime"),
]


def classify(path: str) -> FileKind:
    p = path.replace("\\", "/")
    name = p.rsplit("/", 1)[-1]
    segments = p.split("/")
    if name.endswith(".g.cs") or ".verified." in name or any(
        "snapshot" in s.lower() for s in segments[:-1]
    ):
        return "generated"
    if p.startswith("tests/"):
        return "test"
    if name.lower().endswith(".md") or p.startswith("doc/"):
        return "docs"
    if name.endswith(".tt"):
        return "template"
    return "source"


def area_for(path: str, kind: FileKind | None = None) -> str:
    kind = kind or classify(path)
    if path.startswith("tests/"):
        return "tests"
    if kind == "docs":
        return "docs"
    best = ""
    area = "other"
    for prefix, a in AREA_TABLE:
        if path.startswith(prefix) and len(prefix) > len(best):
            best, area = prefix, a
    return area


_HUNK_RE = re.compile(r"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@ ?(.*)$")


def _strip_prefix(p: str) -> str:
    # git terminates ---/+++ paths that contain spaces with a TAB.
    p = p[:-1] if p.endswith("\t") else p
    return p[2:] if p[:2] in ("a/", "b/") else p


def parse_diff(diff_text: str) -> tuple[list[FileChange], list[Hunk]]:
    files: list[FileChange] = []
    hunks: list[Hunk] = []
    blocks = re.split(r"^(?=diff --git )", diff_text, flags=re.M)
    for block in blocks:
        if not block.startswith("diff --git "):
            continue
        lines = block.split("\n")
        status = "modified"
        old_path = new_path = None
        header_paths = lines[0][len("diff --git "):]
        body_start = len(lines)
        for i, line in enumerate(lines[1:], start=1):
            if line.startswith("@@"):
                body_start = i
                break
            if line.startswith("new file mode"):
                status = "added"
            elif line.startswith("deleted file mode"):
                status = "deleted"
            elif line.startswith("rename from "):
                status, old_path = "renamed", line[len("rename from "):]
            elif line.startswith("rename to "):
                new_path = line[len("rename to "):]
            elif line.startswith("--- ") and line[4:].rstrip("\t") != "/dev/null":
                old_path = old_path or _strip_prefix(line[4:])
            elif line.startswith("+++ ") and line[4:].rstrip("\t") != "/dev/null":
                new_path = _strip_prefix(line[4:])
        if new_path is None:
            if status == "deleted" and old_path:
                new_path = old_path
            else:
                half = header_paths.split(" b/", 1)
                new_path = half[1] if len(half) == 2 else _strip_prefix(header_paths)
        path = new_path
        kind = classify(path)
        adds = dels = 0
        parsed: list[tuple[dict, list[str]]] = []
        for line in lines[body_start:]:
            m = _HUNK_RE.match(line)
            if m:
                meta = {
                    "id": f"{path}#{len(parsed) + 1}", "file": path, "kind": kind,
                    "old_start": int(m[1]), "old_lines": int(m[2]) if m[2] is not None else 1,
                    "new_start": int(m[3]), "new_lines": int(m[4]) if m[4] is not None else 1,
                    "header": m[5].strip(),
                }
                parsed.append((meta, []))
                continue
            if not parsed:
                continue
            if line.startswith("+"):
                adds += 1
            elif line.startswith("-"):
                dels += 1
            parsed[-1][1].append(line)
        if parsed and parsed[-1][1] and parsed[-1][1][-1] == "":
            parsed[-1][1].pop()  # artefact of splitting the diff on newlines
        hunks.extend(Hunk(text="\n".join(body), **meta) for meta, body in parsed)
        files.append(FileChange(path=path, kind=kind, status=status, additions=adds,
                                deletions=dels, area=area_for(path, kind)))
    return files, hunks


def summarize(files: list[FileChange]) -> tuple[dict[str, int], str]:
    counts: dict[str, int] = {k: 0 for k in ("source", "test", "docs", "generated", "template")}
    for f in files:
        counts[f.kind] += 1
    counts["total"] = len(files)
    counts["not_generated"] = len(files) - counts["generated"]
    summary = (f"{counts['generated']} generated files routed to regeneration check; "
               f"{counts['not_generated']} reviewed")
    return counts, summary


def run_intake(change_id: str, requirement_id: str, base: str | None = None,
               head: str | None = None, repo: Path | None = None) -> IntakeReport:
    repo = repo or config.MOCK_REPO
    base_sha = rev_parse(repo, base or config.BASE_SHA)
    head_ref = head or change_id
    head_sha = rev_parse(repo, head_ref)
    diff = git("-c", "core.quotePath=false", "diff", "--no-color", "--no-ext-diff", "-M",
               "--src-prefix=a/", "--dst-prefix=b/", base_sha, head_sha, cwd=repo)
    files, hunks = parse_diff(diff)
    counts, summary = summarize(files)
    return IntakeReport(change_id=change_id, requirement_id=requirement_id, base=base_sha,
                        head=head_ref, head_sha=head_sha, files=files, hunks=hunks,
                        counts=counts, summary=summary)
