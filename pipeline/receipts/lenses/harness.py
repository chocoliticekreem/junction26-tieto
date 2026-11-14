"""What a generated test must know about tests/MessagePack.SourceGenerator.Tests to compile and run.

Facts checked against the project at base 32258fdb (csproj, Usings.cs, Verifiers/*.cs).
"""

TEST_PROJECT_DIR = "tests/MessagePack.SourceGenerator.Tests"
GENERATED_DIR = TEST_PROJECT_DIR + "/Receipts"

HARNESS_NOTES = f"""Test project facts (tests/MessagePack.SourceGenerator.Tests, target net8.0):
- ImplicitUsings and Nullable are enabled. Global usings: System.Collections.Immutable,
  MessagePack.SourceGenerator.CodeAnalysis, Xunit, Xunit.Abstractions. xUnit v2 ([Fact], [Theory],
  ITestOutputHelper). FluentAssertions is referenced.
- Name clash: the global using MessagePack.SourceGenerator.CodeAnalysis brings in types named
  AnalyzerOptions, TypeCollector, GeneratorOptions, ReferenceSymbols, TypeParameter and more. If you
  also import Microsoft.CodeAnalysis.Diagnostics, never write a bare `AnalyzerOptions` (CS0104):
  use `compilation.WithAnalyzers(analyzers)` without options, or the fully qualified name.
- Project references: src/MessagePack.SourceGenerator, src/MessagePack.Analyzers.CodeFixes,
  src/MessagePack. Packages: Microsoft.CodeAnalysis.CSharp.SourceGenerators.Testing.XUnit,
  Microsoft.CodeAnalysis.CSharp.Workspaces (Roslyn APIs: CSharpCompilation, CSharpGeneratorDriver,
  MetadataReference, etc.).
- Helpers available in the test assembly (internal, global namespace):
  * `using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;`
    `new VerifyCS.Test(ReferencesSet references = ReferencesSet.MessagePack)` is a
    CSharpSourceGeneratorTest that also runs ALL MessagePack analyzers. Settable: `LanguageVersion`
    (default CSharp7_3), `TestState.Sources`, `TestState.AdditionalReferences`,
    `TestState.ExpectedDiagnostics`, `TestBehaviors`. It fails on any unexpected diagnostic, including
    compiler warnings, AD0001 (analyzer threw) and CS8785 (generator threw).
  * IMPORTANT: `VerifyCS.Test.RunDefaultAsync(logger, source)` and `RunAsync()` compare generated
    files with embedded snapshot resources named after the test method. New tests have no snapshots,
    so ALWAYS set `TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck`
    (`using Microsoft.CodeAnalysis.Testing;`) and call `await test.RunAsync();` - or drive
    `CSharpGeneratorDriver` yourself and assert on `GetRunResult()`.
  * Diagnostic markup in sources: `{{|MsgPack012:MyObject|}}` expects that diagnostic on that span.
  * `ReferencesHelper.GetReferences(ReferencesSet.MessagePack)` yields MetadataReferences for
    MessagePack.Annotations and MessagePack. `ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp,
    CancellationToken.None)` yields framework references (Microsoft.CodeAnalysis.Testing).
  * A resolver is emitted for a `[GeneratedMessagePackResolver] partial class X {{ }}` declared in the
    source under test (hint name "<Namespace>.X.g.cs"; formatter file names vary).
- Your file is added as a NEW file under {GENERATED_DIR}/. It must be self-contained: do not call
  helpers from other test files except the ones listed above (they exist on base and head). Put your
  class in `namespace Receipts.GeneratedTests;` with a unique class name. Start the file with the
  repository's copyright header:
  // Copyright (c) All contributors. All rights reserved.
  // Licensed under the MIT license. See LICENSE file in the project root for full license information.
- The file must compile on BOTH base and head unless expects is "new-api" (the API under test does not
  exist on base). C# 12 / net8.0. Raw string literals are fine. Source strings compiled inside a test
  are parsed as C# 7.3 unless you set LanguageVersion (records need LanguageVersion.Latest or CSharp9+).
"""
