import { execFileSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { E2E_TMP } from "../playwright.config";

function git(args: string[], cwd: string) {
  execFileSync("git", args, { cwd, stdio: "pipe", env: { ...process.env, GIT_TERMINAL_PROMPT: "0" } });
}

const AUTHOR = ["-c", "user.name=Fixture Bot", "-c", "user.email=fixture@receipts.local"];

/** Bare repo with main + review/ju26-issue-2297/A + review/ju26-issue-2297/B, each one commit ahead of main. */
function createBareRepo(bare: string) {
  const work = fs.mkdtempSync(path.join(os.tmpdir(), "receipts-fixture-repo-"));
  git(["init", "-q", "-b", "main"], work);
  fs.writeFileSync(path.join(work, "README.md"), "# mock repo\n");
  fs.writeFileSync(path.join(work, "FormatterTemplate.cs"), "class FormatterTemplate { }\n");
  git(["add", "."], work);
  git([...AUTHOR, "commit", "-q", "-m", "Base commit"], work);
  for (const side of ["A", "B"]) {
    git(["checkout", "-q", "-b", `review/ju26-issue-2297/${side}`, "main"], work);
    fs.writeFileSync(path.join(work, "FormatterTemplate.cs"), `// interpretation ${side}\n[ExcludeFromCodeCoverage]\nclass FormatterTemplate { }\n`);
    git(["add", "."], work);
    git([...AUTHOR, "commit", "-q", "-m", `REQ-2297 interpretation ${side}`], work);
  }
  git(["checkout", "-q", "main"], work);
  execFileSync("git", ["clone", "-q", "--bare", work, bare], { stdio: "pipe" });
  fs.rmSync(work, { recursive: true, force: true });
}

export default function globalSetup() {
  fs.rmSync(E2E_TMP, { recursive: true, force: true });
  fs.mkdirSync(E2E_TMP, { recursive: true });
  fs.cpSync(path.resolve(__dirname, "fixtures"), E2E_TMP, { recursive: true });
  fs.mkdirSync(path.join(E2E_TMP, "decisions"), { recursive: true });
  createBareRepo(path.join(E2E_TMP, "mock-repo.git"));
}
