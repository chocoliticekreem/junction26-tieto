# Compatibility byte-diff harness: measured

Measured 2026-11-14 on Anson's Mac, .NET SDK 8.0.402 (`~/.dotnet/dotnet`), sample repo base `32258fdb`.

## How it builds
- Builds `src/MessagePack/MessagePack.csproj -f net8.0 -c Release` inside each tree (output `<tree>/bin/MessagePack/Release/net8.0/`). SDK 8.0.402 satisfies the repo's `global.json` (pins 8.0.402, rollForward patch).
- Copies `tools/compat-harness/` into `<run_dir>/compat/harness-<side>/` and builds it with `-p:MessagePackBin=<that folder>` (HintPath references to MessagePack.dll and MessagePack.Annotations.dll). This was chosen over a cross-tree ProjectReference: it avoids the repo's Directory.Build.props, analyzers and multi-targeting, and each side gets its own obj/bin, so base and head never share build state.
- The harness's own `Directory.Build.props` stops props/targets/central package versions from leaking in from parent folders.
- Runtime dependency `Microsoft.NET.StringTools` 17.11.4 is a harness PackageReference (MessagePack references it with PrivateAssets=compile, so it is not in its bin). Restores from the local NuGet cache.

## Timings (seconds)
| Step | Fresh tree, build servers shut down | Warm (tree already built) |
|---|---|---|
| Build MessagePack net8.0 (per tree) | 5.8 | 1.8 |
| Build harness (per side) | 1.3 | 0.7 |
| Serialize 124 cases | 0.2 | 0.2 |
| API listing | 0.06 | 0.06 |
| Round-trip base fixture on head | 0.2 | 0.2 |
| `run_compat` end to end (base built, head fresh) | 10.3 | 7.1 |

pytest with `RUN_DOTNET_TESTS=1` (creates two fresh worktrees per test): 19 s for the two real-harness tests.

## Results
| Head | Bytes differ | Round-trip failures | API +/- | Result |
|---|---|---|---|---|
| `ju26-issue-2129` | 0/124 | 0 | +3 / -0 (`GeneratedMessagePackFormatterAttribute`: type, ctor, `SurrogateType`) | pass |
| Planted: int writer `>= sbyte.MinValue` -> `>` | `int/-128`: `d080` -> `d1ff80` | 0 | 0 | fail |
| Planted: same in the short overload | `short/-128`: `d080` -> `d1ff80` | 0 | 0 | fail |
| Planted: timestamp32 reader adds 1 s | 0 | 4 (`datetime/unspecified`, `datetimeoffset/+02`, `object/nested`, `contractless/poco`) | 0 | fail |

The three plants were applied cumulatively to one throwaway worktree (int writer alone first, then the reader, then the short overload); each row lists the cases that plant changed. The first version of the sample set missed the short-overload plant (it had no `short/-128` case). The set now repeats the encoding boundaries per integer width.

## Fixture
`tools/compat-harness/fixtures/base.json` is the base build's output (124 cases). `run_compat` round-trips it through head. Regenerate after changing the case set:
`cd pipeline && uv run python -m receipts.compat <base_tree> <base_tree> <run_dir> --write-fixture`.
