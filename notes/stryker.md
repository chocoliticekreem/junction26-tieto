# Stryker.NET vs our lightweight mutator: one change (2283)

Measured 2026-11-14 on Anson's Mac (Apple M4 Pro, 12 cores), .NET SDK 8.0.402, Stryker.NET 4.16.0. Change: `ju26-issue-2283` (head `9df83c38`) against base `32258fdb`. Our side: run `runs/ju26-issue-2283/20261114T061534Z` (`mutants.json`, `evidence.json` id `mutation`). Stryker reports, trimmed to the added lines: `notes/stryker-2283-report.json`.

## Short version
- Stryker handles the netstandard2.0 source-generator project with no workaround. It needs nothing beyond `project: MessagePack.SourceGenerator.csproj`, run from the test project folder.
- On the lines the change added, Stryker made 9 mutants on 6 lines; we made 6 mutants on 4 lines. 4 of our 6 mutants have an identical Stryker mutant. The other 2 (deleting the assignment on 239, `if (true)` on 1446) have no Stryker counterpart.
- When both tools ran the same tests (`GenericsFormatterTests`), they agreed on killed vs not killed for all 4 shared mutants. For 2 of them Stryker gives a more exact reason: our "survived" is Stryker's "NoCoverage", meaning the change's own tests never run that line.
- Our 4 survivors are not real gaps in the full suite. With the whole test project, all 6 of our mutants are killed: 4 through the identical Stryker mutant, 2 by a manual run.
- Stryker found 2 survivors on added lines that live through the full suite, and we generated neither: `&&` -> `||` on 1446, and `FirstOrDefault()` -> `First()` on 1434.
- Cost: our 6 mutants took 40.9 s, run one after another. Stryker took 26.5 s (filtered tests) and 34.3 s (full project) at concurrency 2. Both Stryker times include its build and first test run. Stryker is faster because it compiles once and switches mutants in memory.

## Commands
Scratch dir `S=<scratch>/stryker` (a temp folder outside the repo). Every command ran through a wrapper that unsets any `*KEY*`/`*TOKEN*`/`*SECRET*` variable and sets `DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1`.

```sh
# 1. Change at head. The harness refused `git -C mock-repo worktree add` from the isolated agent,
#    so we used a local clone of the same branch instead (same commit, 9df83c38).
git clone --branch ju26-issue-2283 <project>/mock-repo $S/head

# 2. Stryker as a local tool (not global)
dotnet new tool-manifest && dotnet tool install dotnet-stryker            # 5.1.0: fails, see below
dotnet tool install dotnet-stryker --version 4.16.0 --tool-path $S/tools  # used

# 3. Runs, from $S/head/tests/MessagePack.SourceGenerator.Tests
dotnet build -c Debug                                  # warm-up, not timed
$S/tools/dotnet-stryker -f $S/cfg-filtered.json -O $S/out-filtered
$S/tools/dotnet-stryker -f $S/cfg-full.json     -O $S/out-full
```

`cfg-filtered.json` (`cfg-full.json` is the same without `test-case-filter`):
```json
{ "stryker-config": {
    "project": "MessagePack.SourceGenerator.csproj",
    "mutate": ["**/CodeAnalysis/TypeCollector.cs{13122..13278}{49823..50076}{55738..56001}{75110..76514}"],
    "test-case-filter": "FullyQualifiedName~GenericsFormatterTests",
    "concurrency": 2, "additional-timeout": 30000,
    "reporters": ["json", "html", "progress", "cleartext"], "mutation-level": "Standard" } }
```

The `{a..b}` spans in `--mutate` are character offsets, not line numbers. They were computed for the lines the change added: 239, 985, 1077 and 1429-1451 (from `git diff -U0 32258fdb ju26-issue-2283 -- .../TypeCollector.cs`). The reports confirm that every tested mutant falls on those lines.

### Gotchas found
- **Stryker 5.x does not install on SDK 8.0.402.** `dotnet tool install dotnet-stryker` (5.1.0) fails with "Settings file 'DotnetToolSettings.xml' was not found in the package". 4.16.0, the last 4.x, installs and runs.
- **The repo's own `.config/dotnet-tools.json` hides a manifest in a parent folder.** Inside the clone, `dotnet dotnet-stryker` could not find the tool. `--tool-path` fixes it and is still a local install.
- **Safe mode outside the change.** One Stryker mutant in `CollectGeneric` (line 669, CS0165) did not compile, so Stryker marked all mutants in that method as compile errors. Project-wide: 375 compile errors out of 2841 mutants. On the added lines: 0 compile errors and 0 timeouts.

## Results on the lines the change added

| Run | Mutants | Killed | Survived | NoCoverage | Compile err | Timeout | Ignored* | Score | Wall time |
|---|---|---|---|---|---|---|---|---|---|
| Ours (GenericsFormatterTests) | 6 | 2 | 4 | n/a | 0 | 0 | 0 | 2/6 = 33% | 40.9 s (6 x ~6.8 s rebuild + test) |
| Stryker, GenericsFormatterTests (19 tests) | 9 | 2 | 4 | 3 | 0 | 0 | 2 | 2/9 = 22% | 26.5 s |
| Stryker, full project (170 tests) | 9 | 7 | 2 | 0 | 0 | 0 | 2 | 7/9 = 78% | 34.3 s |
| Ours, full project | 6 | 6 | 0 | n/a | 0 | 0 | 0 | 6/6 = 100% | 4 shown through identical Stryker mutants, 2 run by hand (~8-9 s each) |

\* Block-removal mutants on 1445 and 1447 that Stryker dropped ("block already covered"). Stryker's score counts NoCoverage as not detected.

### Agreement, mutant by mutant

| Ours | Line | Operator | Our fate (filtered) | Stryker counterpart | Stryker, filtered | Stryker, full |
|---|---|---|---|---|---|---|
| m1 | 239 | negate-condition (`is null` -> `is not null`) | survived | Equality mutation, identical | Survived | Killed (3 x MsgPack011PartialTypeRequiredTests) |
| m2 | 239 | delete-statement (the assignment) | survived | none (Stryker does not delete assignments) | n/a | n/a; by hand: **killed** (12+ failures: PrivateMemberAccessTests, MsgPack011, MsgPack015) |
| m3 | 985 | delete-statement | survived | Statement mutation, identical | **NoCoverage** | Killed (MsgPack00xAnalyzerTests.NullStringKey) |
| m4 | 1446 | negate-condition | killed | Negate expression, identical | Killed | Killed (10 tests) |
| m5 | 1446 | remove-guard (`if (true)`) | killed | none (nearest: `&&` -> `||`) | n/a | n/a; by hand: **killed** (GenericsFormatterTests.ClosedGenericStruct_FromMetadataReference_UnkeyedMember) |
| m6 | 1448 | delete-statement | survived | Statement mutation, identical | **NoCoverage** | Killed (MsgPack011 x3, GenerationTests) |

Exact status match on the filtered tests: 2 of 4 shared mutants (m1, m4). The other 2 (m3, m6) agree on "not killed", and Stryker says why: the line never runs.

### Mutants only Stryker made (on added lines)

| Line | Mutation | Filtered | Full |
|---|---|---|---|
| 239 | Conditional (true): `true ? null : ...` | Survived | Killed |
| 239 | Conditional (false): `false ? null : ...` | Survived | Killed |
| 1077 | `FirstOrDefault()` -> `First()` | Killed | Killed |
| 1434 | `FirstOrDefault()` -> `First()` in `GetPropertyIdentifierLocation` | NoCoverage | **Survived** |
| 1446 | `IsInSource && ContainsSyntaxTree(...)` -> `\|\|` | **Survived** | **Survived** |

Both full-suite survivors point at real test gaps in the change:
- 1446 `||`: no test reports a diagnostic whose location is in source but in a syntax tree outside the analysed compilation. Swapping the operators changes which diagnostics get dropped, and nothing notices.
- 1434 `First()`: no test reaches `GetPropertyIdentifierLocation` with a property declared in metadata (a referenced-assembly property with neither a string key nor an int key). That is exactly the case the method's null return exists for.

## Conclusion for the concept card
- **Agreement.** 4 of our 6 mutants are identical to Stryker mutants. Fates agree on killed vs not killed for all 4. For 2 of them Stryker's "NoCoverage" is a sharper label than our "survived".
- **What our survivors mean.** They mean "the change's own tests do not check this line". They do not mean "nothing in the repo checks it": the full suite kills all 6 of our mutants. The test-quality lens should word survivors that way, which is the question it asks ("do the change's own tests have teeth?").
- **What we miss.** Stryker's richer operators found 2 survivors on added lines that survive the whole suite (`&&`/`||` swap, `FirstOrDefault`/`First`). Our 5 operators made neither mutation. Each one is a missing test for a path the change added.
- **Cost.** Our mutator rebuilds and reruns once per mutant: 40.9 s for 6. Stryker took 26.5-34.3 s for 9, at concurrency 2, including its build. On this change Stryker is both broader and faster. Our mutator wins on simplicity: no extra tool, no version pinning (5.x does not install on this SDK), and output that is easy to read.
- **One line for the trade-offs box.** "Our 6 mutants: 4 are identical to Stryker's 9 on the added lines, and fates agree on 4 of 4. Stryker found 2 more survivors on added lines that survive even the full suite, and we generated neither. Cost: 40.9 s for us vs 26.5 s for Stryker."
