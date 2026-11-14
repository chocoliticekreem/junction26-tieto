// One-sentence definitions for the jargon that remains on the page. Rendered by <Term>.

export type TermKey = "receipt" | "finding" | "hunch" | "mutant" | "regeneration" | "trust" | "risk" | "signoff" | "ab" | "notchecked" | "blast" | "pinned" | "caller";

export const glossary: Record<TermKey, string> = {
  receipt: "Something that actually ran and left a result: a test, a byte comparison, a rebuilt file or a migration. No receipt, no finding.",
  finding: "A problem with a receipt behind it: a test that fails, a change no test catches, or data that reads differently.",
  hunch: "A suspicion the reviewer could not prove with a receipt. Shown apart, never counted against the change.",
  mutant: "A tiny deliberate break planted in the new code. If every test still passes, the mutant survives and that line is unprotected.",
  regeneration: "Generated files are rebuilt from their templates and compared byte for byte, so nobody has to read them.",
  trust: "How much this area of the code has earned, 0 to 3, from correct past verdicts. Only level 3 can ever skip a sign-off.",
  risk: "How dangerous the touched code is, 1 to 10, from fixed rules plus a small router model. Higher risk gets a deeper review.",
  signoff: "A named person accepts the change exactly as checked, including the listed gaps. The AI never approves on its own.",
  ab: "The requirement reads two ways. Both were built and checked; a named person picks A or B instead of the AI guessing.",
  notchecked: "What the review did not run, and why. Written from what did not execute, never by hand.",
  blast: "Everything outside this change that calls a function it modified or deleted. The wider it is, the more features one change can break.",
  pinned: "A test written from how the old code behaved, before the change. If it still passes on the new code, that behaviour is unchanged; if it fails, the change altered it.",
  caller: "A place in the product that uses a modified function. Tested means an existing test runs through it; untested means nothing would notice if it broke.",
};
