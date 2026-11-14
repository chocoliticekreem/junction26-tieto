# REQ-2297 round 2: implementer fix, re-review blocked on API quota

**Who wrote the fix:** an agent playing the implementing agent (commit author
`Implementing agent (simulated, round 2) <implementer@receipts.local>`). The reviewer did not write
it. No reviewer code, prompt or fixture was changed to influence a verdict.

## What the implementer changed

Branch `ju26-issue-2297-r2` in the mock repo = `ju26-issue-2297` (a0aa5ab2) + one commit `7fc9ebe1`.
The original `ju26-issue-2297` branch was not modified.

The fix addresses only the three verified findings from run `ju26-issue-2297/20261114T061534Z`:

| Finding | Severity | Receipt | Fix in r2 |
|---|---|---|---|
| requirement-tests-1 | high | gentest-2 | Removed `#if !NETSTANDARD2_0 && !NETFRAMEWORK` / `#endif` from every template (.tt and generated .cs, 6 pairs). `ExcludeFromCodeCoverageAttribute` exists on netstandard2.0 and .NET Framework 4.0+. |
| logic-edge-2 | medium | gentest-3 | Same guard removal. |
| logic-edge-1 | high | gentest-3 | The generator checks whether the user's own partial resolver declaration (`[GeneratedMessagePackResolver]` or `[CompositeResolver]`) already has `[ExcludeFromCodeCoverage]`. If it does, the generated part leaves the attribute off, which avoids CS0579. New `ResolverOptions.UserDeclaredExcludeFromCodeCoverage` and `CompositeResolverTemplate.UserDeclaredExcludeFromCodeCoverage`, read by the two resolver templates. |

Other changes:
- Snapshots were regenerated the repo's way: the `WRITE_EXPECTED` define was switched on in `CSharpSourceGeneratorVerifier`1+Test.cs`, the tests were run, then the define was switched off again. Across 222 snapshot files the only change is 804 deleted guard lines (402 `#if`, 402 `#endif`).
- In `CodeCoverageExclusionTests`, the test that required the guard became `ExcludeAttributeIsNotConditionalOnTargetFramework`. Two compile tests were added for a user resolver that already carries the attribute, one for the generated resolver and one for the composite resolver.
- The README sentence claiming the attribute is unavailable on netstandard2.0 and .NET Framework was removed. Removing the guard made it false.

Local gate: `dotnet test tests/MessagePack.SourceGenerator.Tests --framework net8.0` gives 171/171 passed (base 167, original change 169). The build has no new warnings; the only warning is NU1901, which was already there.

## Round-1 receipts re-run against r2 (by the implementer, locally)

The three generated test files from the round-1 run were copied temporarily into the test project and run:

| Receipt test | Round 1 (head a0aa5ab2) | r2 (7fc9ebe1) |
|---|---|---|
| EdgeCase.UserResolverAlreadyExcludedFromCoverage_StillCompiles (logic-edge-1) | fail (CS0579) | pass |
| EdgeCase.GeneratedResolverIsExcludedFromCoverage_WhenTargetDefinesSymbol x2 (logic-edge-2) | fail | pass |
| LegacyTargets.GeneratedTypesAreExcludedEvenWhenLegacyTargetSymbolDefined x2 (requirement-tests-1) | fail: `Test.MyResolver` lacks attribute | **still fails**: `Test.MyResolver.Test` lacks attribute |
| GeneratedCode.AllGeneratedTypesAreExcludedFromCodeCoverage (gentest-1, no finding attached) | fail | still fails, same type |

The resolver itself now carries the attribute on every target. What still fails is the generator's
namespace-mirror nesting class (`partial class Test` emitted inside the resolver so it can hold
formatters). The original change never covered it, and it fails whether or not a target symbol is defined.
r2 deliberately leaves it alone. In round 1 it was evidence with no finding attached (gentest-1), and a
naive fix gives CS0579: each formatter file re-declares that partial class, so putting the attribute
there repeats it. A real fix would declare each mirror class once, for example in the resolver file.
That fix is the r3 candidate if the re-review verifies the defect.

## Round-2 review: not valid, blocked

`uv run receipts review --change ju26-issue-2297-r2 --req REQ-2297` ran (run 20261114T071018Z), but
every Anthropic call returned HTTP 400 "You have reached your specified API usage limits. You will
regain access on 2026-12-01 at 00:00 UTC."

Consequences:
- All six lenses were "unavailable", so no requirement-tests or logic-edge tests were generated.
- The reader panel replayed from its fixture (same requirement), so it still found two readings.
- The A/B builder could not write reading B (`ab-variant-B` fail).
- The receipt still said `decision-A-B` with only branch A built, and `__A` said `needs-sign-off` with
  "no AI lens ran". That verdict is a quota artifact, not a review result.

The non-LLM receipts that did run on r2: build base/head pass; existing tests head 171/171, base
167/167; regeneration 222/222 verified by 89 snapshot tests. Mutation: 1 survivor at
`AnalyzerUtilities.cs:89`, negating the `HasExcludeFromCodeCoverageAttribute` comparison. That mutant
turns the flag on for every resolver, and the existing tests do not notice because
`AllGeneratedTypesAreExcludedFromCodeCoverage` checks per file, not per type. This is a real test gap in r2.

The degraded runs were moved out of `runs/` (to the session scratchpad), so the dashboard does not show
a misleading A/B card. The JEV router fixtures recorded for r2 hunks
(`fixtures/llm/router-jev/*.json`) stay, so a rerun replays them. A side effect of the run is branch
`review/ju26-issue-2297-r2/A` (= r2 head) in the mock repo.

Cost of this round: $0.00 Anthropic, because every call was refused. The JEV router calls are recorded as fixtures.

## Before / after (as far as can be shown without the lenses)

| | Round 1 | Round 2 |
|---|---|---|
| Verdict | blocked (requirement-tests-1, logic-edge-1) | not determined (lenses unavailable) |
| Guard on netstandard2.0 / net4x | attribute dropped | attribute emitted |
| User resolver already `[ExcludeFromCodeCoverage]` | CS0579 | compiles |
| Namespace-mirror nesting class | no attribute | no attribute (unchanged, unverified in round 1) |
| Existing SourceGenerator tests | 169/169 | 171/171 |

## To finish round 2

Once the key has quota again (or with a different key in `.env.local`), run:

```
uv run receipts review --change ju26-issue-2297-r2 --req REQ-2297
```

Then check the replay into a scratch dir:

```
RECEIPTS_RUNS=<scratch> MOCK_LLM=1 uv run receipts review --change ju26-issue-2297-r2 --req REQ-2297
```

Expect requirement-tests-1's receipt to stay red on the mirror class unless the new lens tests differ.
