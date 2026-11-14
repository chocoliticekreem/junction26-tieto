import type { Evidence, EvidenceKind } from "./types";

/** A passing receipt that argues FOR the change, with a one-line label derived from its data. */
export interface ProvenLine {
  id: string;
  label: string;
}

const POSITIVE_KINDS: EvidenceKind[] = ["fail-before-pass-after", "new-api", "regeneration", "byte-diff", "migration-up-down", "row-preservation", "behaviour-preserved"];

function num(v: unknown): number | undefined {
  return typeof v === "number" ? v : undefined;
}

function len(v: unknown): number | undefined {
  return Array.isArray(v) ? v.length : undefined;
}

/** Last "N/M" in the title, which the pipeline writes as "fails on base (4/4), passes on head (4/4)". */
function ratioFromTitle(title: string): string | undefined {
  const m = [...title.matchAll(/(\d+)\/(\d+)/g)].pop();
  return m ? `${m[1]}/${m[2]}` : undefined;
}

function testCount(ev: Evidence): string | undefined {
  const fromTitle = ratioFromTitle(ev.title);
  if (fromTitle) return fromTitle;
  const tests = ev.details?.tests;
  if (Array.isArray(tests) && tests.length) {
    const passed = tests.filter((t) => (t as Record<string, unknown>).head === "passed").length;
    return `${passed}/${tests.length}`;
  }
  const m = ev.title.match(/\((\d+) tests?\)/);
  return m ? `${m[1]} tests` : undefined;
}

function describe(ev: Evidence): { text: string; count?: string } {
  const d = ev.details ?? {};
  switch (ev.kind) {
    case "fail-before-pass-after":
      if (d.expects === "fail-on-head") return { text: "Edge-case tests pass on head, no defect shown", count: testCount(ev) };
      return { text: "Requirement tests fail on base, pass on head", count: testCount(ev) };
    case "new-api":
      return { text: "Tests pass on head for the new API (weaker evidence)", count: testCount(ev) };
    case "regeneration": {
      const total = num(d.generated_files) ?? num(d.generated_files_count);
      const ok = num(d.covered) ?? (total !== undefined && num(d.mismatched) !== undefined ? total - (num(d.mismatched) as number) : undefined);
      return { text: "Generated files verified against head", count: total !== undefined && ok !== undefined ? `${ok}/${total} files` : undefined };
    }
    case "byte-diff": {
      const total = num(d.cases) ?? num(d.fixtures);
      const differing = num(d.differing) ?? len(d.differing);
      return { text: "Serialized bytes identical on base and head", count: total !== undefined && differing !== undefined ? `${total - differing}/${total} cases` : undefined };
    }
    case "migration-up-down": {
      const objects = num(d.schema_objects);
      return { text: "Down migration restores the base schema", count: objects !== undefined ? `${objects} schema objects` : undefined };
    }
    case "row-preservation": {
      const columns = len(d.value_checksums);
      if (columns !== undefined) return { text: "Column values preserved across the migration", count: `${columns} ${columns === 1 ? "column" : "columns"}` };
      const tables = len(d.row_counts);
      return { text: "No rows lost across the migration", count: tables !== undefined ? `${tables} ${tables === 1 ? "table" : "tables"}` : undefined };
    }
    case "behaviour-preserved": {
      const member = typeof d.member === "string" ? d.member : ev.title.match(/Characterization: (\S+)/)?.[1];
      const tests = Array.isArray(d.tests) ? d.tests.length : Array.isArray(d.test_names) ? d.test_names.length : undefined;
      return { text: member ? `Old behaviour of ${member} still holds` : "Old behaviour still holds", count: tests !== undefined ? `${tests}/${tests} pinned tests pass` : undefined };
    }
    default:
      return { text: ev.title };
  }
}

export function provenLines(evidence: Evidence[]): ProvenLine[] {
  return evidence
    .filter((ev) => ev.result === "pass" && POSITIVE_KINDS.includes(ev.kind))
    .map((ev) => {
      const { text, count } = describe(ev);
      return { id: ev.id, label: count ? `${text} · ${count}` : text };
    });
}
