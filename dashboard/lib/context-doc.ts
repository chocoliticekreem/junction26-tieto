// REVIEW_CONTEXT.md as the pipeline writes it: a title, a "Status: ..." line, then sections of bullets
// shaped "[area: a, b] text (source: path)". The page owns the title and the status, so both leave the body.

import { stripHtmlComments } from "./humanize";

export interface ContextDoc {
  /** The document without its title, status line and HTML comments. */
  body: string;
  /** What the status line says beyond the draft/confirmed word, e.g. "Built from the sample repo at 32258fdb ...". */
  provenance?: string;
}

export function parseContextDoc(markdown: string): ContextDoc {
  const lines = stripHtmlComments(markdown).split("\n");
  let provenance: string | undefined;
  const kept: string[] = [];
  let titleSeen = false;
  for (const line of lines) {
    if (!titleSeen && /^#\s+/.test(line)) {
      titleSeen = true;
      continue;
    }
    if (provenance === undefined && /^Status:/i.test(line.trim())) {
      const rest = line
        .trim()
        .replace(/^Status:\s*\w+/i, "")
        .replace(/^\s*\([^)]*\)/, "")
        .replace(/^[\s.,:;]+/, "")
        .trim();
      provenance = rest ? rest[0].toUpperCase() + rest.slice(1) : "";
      continue;
    }
    kept.push(line);
  }
  return { body: kept.join("\n").replace(/^\s+/, ""), provenance: provenance || undefined };
}

/** "[area: analyzer, generator] text" and "text (source: README.md)" — the two tags a context bullet carries. */
export const AREA_TAG = /^\s*\[area:\s*([^\]]+)\]\s*/;
export const SOURCE_TAG = /\s*\(source:\s*([^)]+)\)\s*$/;

export function splitAreas(area: string): string[] {
  return area
    .split(/,\s*/)
    .map((a) => a.trim())
    .filter(Boolean);
}
