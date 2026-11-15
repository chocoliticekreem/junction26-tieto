# The one missed drift plant (cold bench, Anthropic): mock-2297-drift

Run: `runs/mock-2297-drift/20261114T200420Z`. Under metrics.classify it was missed: the verdict was
`blocked`, but the clean run of 2297 is blocked too, and no verified finding sat at the plant.
`missed_but_hunch_at_plant = 1` in T2.

## What the plant is
The change decorates formatters only. The resolver and composite-resolver templates are left as
they are on base. REQ-2297's first acceptance criterion says "All generated code is decorated with
`ExcludeFromCodeCoverage`". The change's own tests and README are narrowed to formatters, so every
existing test passes.

## Why it was missed
The reviewer found the drift and ran a test that shows it. One check then threw the receipt away.

1. requirement-tests wrote `Req2297CoverageGapTests.ResolverIsExcludedFromCodeCoverage` from the
   requirement. It compiled. It **failed on head** (evidence `gentest-2`): "The generated resolver
   should be decorated with ExcludeFromCodeCoverage."
2. Finding `requirement-tests-2` claims the resolver is left undecorated, and its receipt_plan
   names exactly that test. But the lens put the finding at **`README.md:1639`**. An omission has
   no changed line in code: the resolver template is not in the diff at all. The README sentence
   ("All generated formatters...") was the only visible line where the narrowing shows.
3. `checks.test_backs` refuses any finding located in a docs file ("a test never proves a claim
   about documentation"). This guard was added in #13 for a real 2297 false positive: a "README
   states a false limitation" claim had been verified by a test about generated code. So the
   finding stayed an unverified hunch (medium, verdict_depends_on_intent=true). The verdict was
   blocked only by the netstandard-guard finding, which the clean run has too.

The tests were not about adjacent behaviour, they did not fail to compile, and they did not pass on
head. The test was right and failed on head. What went wrong was where the finding was placed.

Not provider-specific: the Verda cold run of the same case (`runs/mock-2297-drift@verda/
20261114T203756Z`) did the same thing. Its `requirement-tests-1` (high) sat at `README.md:1640`,
backed in substance by a failing `gentest-2`, and stayed a hunch.

Across every committed bench run, 6 test-backed requirement-tests claims were dropped by the docs
guard. Four are code claims that the lens put on a README line ("only formatters are decorated",
"the change does not implement the core fix"). Two (2234) really are README claims. The guard
cannot tell these apart from the location alone, so it stays. The fix belongs at the source of the
claim.

## Fix prototyped (prompt only: `pipeline/receipts/lenses/requirement_tests.py`)
- Tests: work from the acceptance criteria. Write one test method per behavioural criterion and
  check it as written, at its full scope ("all X" asserts over every X the code produces, not only
  the X the diff touches). 1-5 methods per file (was 1-4).
- Findings: a claim about code behaviour, including behaviour the change leaves out, is located at
  the source or template file that produces it, even when the diff does not touch that file.
  Never on a docs line, because such a claim cannot be backed by a test.

No change to checks, verdict, metrics or the docs guard. The rule stays "no receipt, no finding".

## Results (scratch runs, cold, --no-ab; never written into runs/)
| case | provider | verdict | catch (metrics.classify) | evidence |
|---|---|---|---|---|
| drift | Anthropic | blocked | **caught**, requirement-tests + fail-before-pass-after | `requirement-tests-1` high at `Transforms/ResolverTemplate.tt`, `gentest-1` fails on head: "Undecorated generated types: TestNs.MyResolver.g.cs: MyResolver" |
| clean | Anthropic | blocked (as before) | no new finding; criterion test `gentest-1` passes on head | only the known netstandard-guard findings (baseline) |
| control | Anthropic | blocked (as before) | 0 false alarms, 0 off-target | same baseline findings; all-types test `gentest-1` passes on head |
| drift | Verda | blocked | **caught** (requirement-tests-1 + logic-edge-1 at ResolverTemplate.tt) | gentest-2, gentest-3 |
| clean | Verda | needs-sign-off | no finding | requirement test did not compile (kimi variance) |
| control | Verda | blocked | 0 false alarms; 2 off-target, both the real netstandard-guard defect (notes/bench.md) | gentest-2, gentest-3 |

## Cost
Spent: Anthropic $0.88 (drift live $0.27 requirement-tests + $0.30 regression lens, which had no
fixture; clean and control via batch $0.16 + $0.15). Verda €1.00.
Full re-run with the new prompt: every requirement-tests call is a new fixture. The cold dry run
gives 29 requests, **$3.85 batch / $7.69 live**. Warm (18 cases) is about +$1.9 batch, so ~$5.8 batch
in total. Wall time at the last bench's pace: ~58 min cold + ~17 min warm (jobs=3), plus batch
latency.
The offline demo (`MOCK_LLM=1`) loses its requirement-tests fixtures for every change until they
are re-recorded, including the 4 sample branches.
