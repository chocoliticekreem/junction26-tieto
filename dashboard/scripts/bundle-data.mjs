// Copies the run data the dashboard reads into dashboard/data/ so a deploy rooted at dashboard/ can serve it.
// Runs as `prebuild`; does nothing unless RECEIPTS_BUNDLE=1.
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

if (process.env.RECEIPTS_BUNDLE !== "1") process.exit(0);

const dashboard = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const src = path.resolve(dashboard, "..");
const out = path.join(dashboard, "data");
const MAX_FILE = 1024 * 1024;
const MAX_BUNDLE = 60 * 1024 * 1024;

// The Vercel build uploads only dashboard/, so ../runs is absent there: reuse the bundle made before upload.
if (!fs.existsSync(path.join(src, "runs"))) {
  if (fs.existsSync(path.join(out, "runs"))) {
    console.log(`bundle-data: ${path.join(src, "runs")} not found; using the existing bundle in data/ (${mb(dirSize(out))} MB)`);
    process.exit(0);
  }
  console.error(`bundle-data: no ${path.join(src, "runs")} and no data/runs; refusing to build an empty demo`);
  process.exit(1);
}

let skipped = 0;

function copyFile(from, to) {
  if (fs.statSync(from).size > MAX_FILE) {
    skipped += 1;
    return;
  }
  fs.mkdirSync(path.dirname(to), { recursive: true });
  fs.copyFileSync(from, to);
}

function copyTree(from, to, keep = () => true) {
  if (!fs.existsSync(from)) return;
  for (const e of fs.readdirSync(from, { withFileTypes: true })) {
    const a = path.join(from, e.name);
    const b = path.join(to, e.name);
    if (e.isDirectory()) copyTree(a, b, keep);
    else if (e.isFile() && keep(e.name)) copyFile(a, b);
  }
}

function dirSize(dir) {
  let n = 0;
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    n += e.isDirectory() ? dirSize(p) : fs.statSync(p).size;
  }
  return n;
}

function mb(bytes) {
  return (bytes / 1024 / 1024).toFixed(1);
}

const isJson = (name) => name.endsWith(".json");

/** The logs the log viewer can open: CommandReceipt.log_path values that stay inside the run dir. */
function loggedPaths(runDir) {
  let commands;
  try {
    commands = JSON.parse(fs.readFileSync(path.join(runDir, "commands.json"), "utf8"));
  } catch {
    return [];
  }
  if (!Array.isArray(commands)) return [];
  const paths = new Set();
  for (const c of commands) {
    const p = typeof c?.log_path === "string" ? path.normalize(c.log_path) : "";
    if (!p || path.isAbsolute(p) || p.startsWith("..")) continue;
    if (fs.existsSync(path.join(runDir, p)) && fs.statSync(path.join(runDir, p)).isFile()) paths.add(p);
  }
  return [...paths];
}

// Mirrors listChangeIds() in lib/data.ts: a folder with a `latest` file is a change; only its latest run is copied.
function copyRuns(fromRoot, toRoot) {
  let changes = 0;
  const walk = (dir, depth) => {
    if (depth > 6) return;
    const entries = fs.readdirSync(dir, { withFileTypes: true });
    if (entries.some((e) => e.isFile() && e.name === "latest")) {
      const rel = path.relative(fromRoot, dir);
      const ts = fs.readFileSync(path.join(dir, "latest"), "utf8").trim();
      const runDir = path.join(dir, ts);
      if (!ts || !fs.existsSync(path.join(runDir, "receipt.json"))) return;
      copyFile(path.join(dir, "latest"), path.join(toRoot, rel, "latest"));
      for (const e of fs.readdirSync(runDir, { withFileTypes: true })) {
        if (e.isFile() && isJson(e.name)) copyFile(path.join(runDir, e.name), path.join(toRoot, rel, ts, e.name));
      }
      for (const logPath of loggedPaths(runDir)) copyFile(path.join(runDir, logPath), path.join(toRoot, rel, ts, logPath));
      changes += 1;
      return;
    }
    for (const e of entries) if (e.isDirectory()) walk(path.join(dir, e.name), depth + 1);
  };
  walk(fromRoot, 0);
  return changes;
}

function removeTrx(dir) {
  let n = 0;
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) n += removeTrx(p);
    else if (e.name.endsWith(".trx")) {
      fs.rmSync(p);
      n += 1;
    }
  }
  return n;
}

fs.rmSync(out, { recursive: true, force: true });
const changes = copyRuns(path.join(src, "runs"), path.join(out, "runs"));
copyTree(path.join(src, "context"), path.join(out, "context"), (n) => !n.startsWith("."));
copyTree(path.join(src, "decisions"), path.join(out, "decisions"), isJson);
copyTree(path.join(src, "metrics"), path.join(out, "metrics"), isJson);
for (const d of ["runs", "context", "decisions", "metrics"]) fs.mkdirSync(path.join(out, d), { recursive: true });

let size = dirSize(out);
let trx = 0;
if (size > MAX_BUNDLE) {
  trx = removeTrx(path.join(out, "runs"));
  size = dirSize(out);
}
console.log(
  `bundle-data: ${changes} changes, ${mb(size)} MB in data/` +
    (skipped ? `, ${skipped} files over 1 MB skipped` : "") +
    (trx ? `, ${trx} .trx files dropped` : ""),
);
