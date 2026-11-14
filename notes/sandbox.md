# .NET sandbox timings

Measured 2026-11-14 on Anson's Mac (Apple Silicon), .NET SDK 8.0.402 at `~/.dotnet`, NuGet cache warm,
via `receipts.sandbox` (`dotnet build|test <proj> -f net8.0`, `DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1`).
Worktree `work/measure-base` at base `32258fdb`, `bin/` and `obj/` deleted before the clean build.

| Step | Build servers cold (`dotnet build-server shutdown` first) | Build servers warm |
|---|---|---|
| Clean build of `tests/MessagePack.SourceGenerator.Tests` (6 projects) | 7.93 s | 4.28 s |
| Incremental build, nothing changed | 2.63 s | 2.59 s |
| `dotnet test --no-build` SourceGenerator.Tests (167/167 pass) | 6.02 s | 5.09 s |
| `dotnet test` incl. incremental build | 9.36 s | 6.98 s |

First build of a fresh head worktree during `receipts review`: 4.23 s (ju26-issue-2283), 5.19 s
(ju26-issue-2297). A full `receipts review --router rule` (intake, routing, 2 builds, 2 test runs)
takes 17-20 s wall clock.

## `tests/MessagePack.SourceGenerator.ExecutionTests`: out of scope

It builds fast (2.98 s after the SourceGenerator.Tests dependencies are built) but **does not compile,
even on base `32258fdb`**: 8 x `CS0117 'MyGeneratedCompositeResolver' does not contain a definition
for 'Instance'`. Cause, from the log: `warning CS9057: The analyzer assembly
.../MessagePack.SourceGenerator.dll references version '4.12.0.0' of the compiler, which is newer
than the currently running version '4.11.0.0'`. SDK 8.0.402 ships Roslyn 4.11, so the source
generator is not loaded into consumer projects and no generated code exists. The SourceGenerator.Tests
project is unaffected because it drives the generator in-process through its own Roslyn packages.

Consequence: changes that add execution tests (2234, 2129) cannot have those tests run here. The
verdict lists the project under "not checked" with this reason. Fix if needed: install an SDK whose
compiler is Roslyn >= 4.12 (e.g. SDK 9.0.1xx) and relax `global.json`; not done (dependency install
needs Anson's OK).
