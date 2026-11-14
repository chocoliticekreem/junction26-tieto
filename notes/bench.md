# Bench plants: verification log

36 cases: 4 issues x 9 variants (clean plus 8 plants), no capping. Labels are in `bench/manifest.json`, patches in `bench/plants/<issue>/<variant>.patch`, and the bait conventions in `bench/bait-conventions.md`.

## How each plant was verified

- Agent heads: 2283 `9df83c38`, 2234 `9007cee1`, 2129 `083afd99`, 2297 `a0aa5ab2`. Base `32258fdb`.
- Each patch was captured from a worktree, the worktree was reset, and then the patch was re-applied with `git apply --check` followed by `git apply`.
- Build: `~/.dotnet/dotnet build tests/MessagePack.SourceGenerator.Tests/... -f net8.0` (SDK 8.0.402).
- Own tests: `dotnet test --no-build` on the same project. This means the change's own tests plus the existing 167.
- compat, security and the 2129 bait (which touch `src/MessagePack` or Annotations) were also built with `dotnet build src/MessagePack/MessagePack.csproj -f net8.0`. That build was sanity-checked: a deliberately broken file fails it.
- `pipeline/tests/test_bench.py` re-checks that every patch applies to `origin/ju26-issue-<n>` in the sample clone. It uses a throwaway index, so the clone is never modified.
- Timings are for incremental builds in a worktree that was already built once. Fresh first builds per branch took 4-5 s for build plus 3-7 s for tests on this machine. Clean heads: 2283 170/170, 2234 175/175, 2129 173/173, 2297 169/169.

## Results

| id | build | test run | own tests | note |
|---|---|---|---|---|
| mock-2283-logic | ok 4 s | 6 s | 169/170, fails `ClosedGenericStruct_FromMetadataReference_UnkeyedMember` | the change's own test catches it |
| mock-2283-edge | ok 5 s | 6 s | 170/170 | |
| mock-2283-compat | ok 5 s, src/MessagePack ok 3 s | 7 s | 170/170 | |
| mock-2283-security | ok 5 s, src/MessagePack ok 2 s | 6 s | 170/170 | |
| mock-2283-missing-test | ok 7 s | 4 s | 169/169 | logic mutant survives (169/169) |
| mock-2283-drift | ok 4 s | 5 s | 170/170 | |
| mock-2283-bait | ok 4 s | 4 s | 170/170 | |
| mock-2283-control | ok 4 s | 6 s | 170/170 | |
| mock-2234-logic | ok 4 s | 5 s | 175/175 | |
| mock-2234-edge | ok 3 s | 5 s | 175/175 | |
| mock-2234-compat | ok 3 s, src/MessagePack ok 2 s | 5 s | 175/175 | |
| mock-2234-security | ok 5 s, src/MessagePack ok 2 s | 5 s | 175/175 | |
| mock-2234-missing-test | ok 4 s | 7 s | 174/174 | `continue`-removal mutant survives (174/174); killed with the test present (174/175) |
| mock-2234-drift | ok 4 s | 7 s | 175/175 | tests rewritten to match the drift; without the rewrite, 6 of the change's tests fail |
| mock-2234-bait | ok 3 s | 6 s | 175/175 | |
| mock-2234-control | ok 3 s | 6 s | 175/175 | |
| mock-2129-logic | ok 3 s | 4 s | 173/173 | |
| mock-2129-edge | ok 3 s | 6 s | 173/173 | |
| mock-2129-compat | ok 4 s, src/MessagePack ok 2 s | 6 s | 173/173 | |
| mock-2129-security | ok 3 s, src/MessagePack ok 2 s | 4 s | 173/173 | |
| mock-2129-missing-test | ok 3 s | 5 s | 172/172 | type-check mutant survives (172/172); killed with the test present (172/173) |
| mock-2129-drift | ok 3 s | 6 s | 173/173 | |
| mock-2129-bait | ok 4 s, src/MessagePack ok 2 s | 4 s | 173/173 | |
| mock-2129-control | ok 4 s | 6 s | 173/173 | |
| mock-2297-logic | ok 3 s | 4 s | 160/169, 9 enum/nullable snapshot tests fail | snapshots catch it |
| mock-2297-edge | ok 3 s | 4 s | 168/169, fails `GenericDataTypeWithPrivateMember` | snapshot catches it |
| mock-2297-compat | ok 3 s, src/MessagePack ok 2 s | 7 s | 169/169 | |
| mock-2297-security | ok 4 s, src/MessagePack ok 2 s | 5 s | 169/169 | |
| mock-2297-missing-test | ok 4 s | 7 s | 167/167 | deliberately weak: the UnionTemplate mutant is still killed by 4 snapshot tests |
| mock-2297-drift | ok 3 s | 4 s | 169/169 | 91 resolver snapshots regenerated (`WRITE_EXPECTED`) |
| mock-2297-bait | ok 3 s | 3 s | 170/170 | adds 1 test |
| mock-2297-control | ok 3 s | 3 s | 169/169 | |

Every plant compiles. Own tests pass in 29 of 32 plants. In the other 3 (2283-logic, 2297-logic, 2297-edge), the change's own or snapshot tests already catch the plant.

## Byte and security effects, measured

A probe console app (`ContractlessStandardResolver`, net8.0) was built against `src/MessagePack` with each plant applied. The rows below show only the lines that differ from the clean head.

| plant | case | clean head | with plant |
|---|---|---|---|
| 2283-compat | int 200 | `CCC8` | `CD00C8` |
| 2234-compat | Guid `01234567-89ab-...` | `D924 3031...` | `DA0024 3031...` |
| 2129-compat | DateTime 2020-01-01T00:00:00Z | `D6FF5E0BE100` | `D7FF000000005E0BE100` |
| 2297-compat | decimal 1.5 / 12.345 | `A3312E35` / `A631322E333435` | `A4312E3530` / `A531322E3335` ("12.35") |
| 2283-security | `UntrustedData.MaximumObjectGraphDepth` | 500 | 5000 |
| 2234-security | 4 nested arrays, max depth 3 | rejected | accepted |
| 2129-security | `UntrustedData.GetEqualityComparer<Version>()` | `TypeAccessException` | `GenericEqualityComparer` |
| 2297-security | `UntrustedData.WithMaximumObjectGraphDepth(100).HashCollisionResistant` | True | False |

Unchanged by 2283-compat: `int[] {1,200}` (array formatter path) and `int 5`. Unchanged by 2234-compat: a 40-char string written through `Write(string)`.

## Why each planted defect is real

2283 (TypeCollector: drop diagnostics outside the compilation):
- logic: the inverted filter reports diagnostics that have no source location, which is exactly the metadata-member case the change exists to drop. Consumers get MsgPack004 warnings they cannot fix.
- edge: the indexer and hard cast come back. A metadata property with `[Key((string)null)]` throws IndexOutOfRange, and a positional record property throws InvalidCast. Both crash the analyzer (AD0001), the original symptom. The doc comment still promises `null`.
- drift: closed generics from referenced assemblies are skipped instead of analyzed. Nothing crashes and the tests pass, but the generated resolver no longer covers `Item<int>`, the case #2283 reports.
- missing-test: the only test that exercises the drop path is gone, so a mutant of the filter survives.

2234 (analyzer must work for SuppressSourceGeneration types):
- logic: string-keyed public fields are never added to the member map, so generated formatters silently drop them (data loss). The tests only use properties.
- edge: duplicate int keys on fields hit `Dictionary.Add` again (ArgumentException, AD0001), the bug class the change fixes.
- drift: suppressed types are skipped entirely, which is the opposite of the requirement. The tests were rewritten to bless it, and the README still claims validation.
- missing-test: the new `continue` loses its only test.

2129 (generated formatters for referenced-assembly types):
- logic: contractless mode picks public readonly fields (the generated Deserialize assigns them, a compile error in user code) and skips public writable fields (data loss).
- edge: "exactly one IMessagePackFormatter<T>" becomes "at least one". A formatter with two interfaces gets a silently chosen data type instead of MsgPack020.
- drift: types without syntax in the compilation are rejected. That is every type from a referenced assembly, which is the feature itself. Same-project types still work, so the tests pass.
- missing-test: the surrogate type-equality check loses its only test.

2297 (ExcludeFromCodeCoverage on generated code):
- logic: the inverted `#if` puts the attribute only on netstandard2.0/.NET Framework and never on net8.0.
- edge: a nesting type with generic constraints gets `{#if ...` on one line. A preprocessor directive must start a line, so the generated code does not compile.
- drift: formatters only, as in the SPEC example. Resolvers stay in coverage, and all tests (regenerated) pass.
- missing-test: a weak plant on purpose (see the results table). It works as a negative control for the test-quality lens.

compat (runtime, all four): each plant changes the encoding the writer chooses and still reads back. A byte-diff harness over int, Guid, DateTime and decimal catches each one, as shown in the measured table. 2129's mask typo also writes `timestamp32(0)`, decoded as 1970, for values whose low 32 bits are zero. 2297's 'F' format loses decimal precision.

security (runtime, all four):
- 2283 raises the untrusted depth limit above the roughly 2000-frame crash point that the class's own docs describe.
- 2234 lets the depth check accept one level beyond the configured maximum.
- 2129 removes the failsafe that rejects hash keys with no collision-resistant comparer (the hash-flooding DoS defence).
- 2297 makes every `With...` clone of `UntrustedData` lose collision resistance.

bait and control: see `bench/bait-conventions.md`. All bait and control patches leave behaviour unchanged for the existing tests (all pass).

## Findings outside the plants

- `tests/MessagePack.SourceGenerator.ExecutionTests` does not build with SDK 8.0.402, even on the clean 2129 head. The generator assembly references Roslyn 4.12, and the SDK compiler is 4.11 (warning CS9057), so the generator is not loaded and the partial formatters stay unimplemented (CS0535). Execution tests need a newer SDK. The fast project is unaffected because it drives the generator in-process.
- On the clean 2297 head, the `#if !NETSTANDARD2_0 && !NETFRAMEWORK` guard is unnecessary. `ExcludeFromCodeCoverageAttribute` exists in `netstandard.dll` 2.0.3 and in the .NET Framework 4.7.2 `System.dll` (checked in the local reference packs). Generated code for those targets is therefore left in coverage. This is a candidate finding for the honest review of `mock-2297-clean`.
