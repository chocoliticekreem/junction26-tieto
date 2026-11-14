import path from "node:path";

function resolveDir(envName: string, fallback: string): string {
  return path.resolve(process.cwd(), process.env[envName] || fallback);
}

export const dirs = {
  runs: () => resolveDir("RECEIPTS_RUNS", "../runs"),
  context: () => resolveDir("RECEIPTS_CONTEXT", "../context"),
  decisions: () => resolveDir("RECEIPTS_DECISIONS", "../decisions"),
  metrics: () => resolveDir("RECEIPTS_METRICS", "../metrics"),
  mockRepo: () => resolveDir("RECEIPTS_MOCK_REPO", "../mock-repo"),
};

/** Change ids are opaque but become folder paths; reject anything that could escape. */
export function isSafeId(id: string): boolean {
  if (!id || id.length > 200) return false;
  if (id.startsWith("/") || id.includes("\\") || id.includes("\0")) return false;
  return id.split("/").every((seg) => seg !== "" && seg !== "." && seg !== "..");
}

/** Resolve `rel` under `root`, refusing paths that leave `root`. */
export function safeJoin(root: string, rel: string): string | null {
  const full = path.resolve(root, rel);
  const rootWithSep = root.endsWith(path.sep) ? root : root + path.sep;
  if (!full.startsWith(rootWithSep)) return null;
  return full;
}
