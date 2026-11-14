// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using MessagePack.SourceGenerator.Analyzers;

namespace Receipts.GeneratedTests;

public class MetadataGenericValueTypeCrashTests
{
    [Fact]
    public async Task ClosedGenericStructFromReferencedAssembly_GeneratesResolverWithoutCrashes()
    {
        const string ItemDeclaration = """
            using MessagePack;
            namespace Lib;

            [MessagePackObject]
            public partial struct Item<T>
            {
                [Key(0)]
                public string Key { get; set; }
            }
            """;

        await RunReproAsync(ItemDeclaration);
    }

    [Fact]
    public async Task ClosedGenericRecordStructFromReferencedAssembly_GeneratesResolverWithoutCrashes()
    {
        const string ItemDeclaration = """
            using MessagePack;
            namespace Lib;

            [MessagePackObject]
            public partial record struct Item<T>([property: Key(0)] string Key);
            """;

        await RunReproAsync(ItemDeclaration);
    }

    private static async Task RunReproAsync(string itemDeclaration)
    {
        ImmutableArray<MetadataReference> frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        ImmutableArray<MetadataReference> references = frameworkReferences.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        CSharpParseOptions parseOptions = new(LanguageVersion.Latest);

        CSharpCompilation libraryCompilation = CreateCompilation("Lib", itemDeclaration, references, parseOptions);
        libraryCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        const string AppSource = """
            using MessagePack;
            using Lib;
            namespace App;

            [MessagePackObject]
            public sealed partial record Cmd([property: Key(0)] string Scope)
            {
                [Key(1)]
                public Item<int> Member { get; init; }
            }

            [GeneratedMessagePackResolver]
            public partial class TestResolver
            {
            }
            """;

        CSharpCompilation appCompilation = CreateCompilation(
            "App",
            AppSource,
            references.Add(libraryCompilation.ToMetadataReference()),
            parseOptions);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator(),
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            appCompilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> generatorDiagnostics);

        CompilationWithAnalyzers compilationWithAnalyzers = appCompilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new MsgPack00xMessagePackAnalyzer()));
        ImmutableArray<Diagnostic> analyzerDiagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

        generatorDiagnostics.Where(d => d.Id == "CS8785").Should().BeEmpty();
        analyzerDiagnostics.Where(d => d.Id == "AD0001").Should().BeEmpty();
        driver.GetRunResult().GeneratedTrees.Select(t => t.FilePath).Should().Contain(
            path => path.EndsWith("App.TestResolver.g.cs", StringComparison.Ordinal));
        outputCompilation.Should().NotBeNull();
    }

    private static CSharpCompilation CreateCompilation(
        string assemblyName,
        string source,
        ImmutableArray<MetadataReference> references,
        CSharpParseOptions parseOptions)
    {
        return CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }
}
