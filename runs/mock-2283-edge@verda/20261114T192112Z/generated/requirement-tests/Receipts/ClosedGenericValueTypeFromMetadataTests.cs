// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using MessagePack.SourceGenerator.Analyzers;

namespace Receipts.GeneratedTests;

public class ClosedGenericValueTypeFromMetadataTests
{
    private const string AppSource = """
        using MessagePack;
        using Lib;

        namespace App;

        [GeneratedMessagePackResolver]
        public partial class TestResolver
        {
        }

        [MessagePackObject]
        public sealed partial record Cmd([property: Key(0)] string Scope)
        {
            [Key(1)]
            public Item<int> Member { get; init; }
        }
        """;

    [Fact]
    public async Task ClosedGenericRecordStructFromReferencedAssembly_GeneratesResolverWithoutCrash()
    {
        const string librarySource = """
            using MessagePack;

            namespace Lib;

            [MessagePackObject]
            public partial record struct Item<T>([property: Key(0)] string Key);
            """;

        (CSharpCompilation appCompilation, CSharpParseOptions parseOptions) = await CreateAppCompilationAsync(librarySource);
        await AssertCleanGeneratorAndAnalyzerAsync(appCompilation, parseOptions);
    }

    [Fact]
    public async Task ClosedGenericStructFromReferencedAssembly_GeneratesResolverWithoutCrash()
    {
        const string librarySource = """
            using MessagePack;

            namespace Lib;

            [MessagePackObject]
            public partial struct Item<T>
            {
                public Item(string key) => Key = key;

                [Key(0)]
                public string Key { get; }
            }
            """;

        (CSharpCompilation appCompilation, CSharpParseOptions parseOptions) = await CreateAppCompilationAsync(librarySource);
        await AssertCleanGeneratorAndAnalyzerAsync(appCompilation, parseOptions);
    }

    private static async Task AssertCleanGeneratorAndAnalyzerAsync(CSharpCompilation appCompilation, CSharpParseOptions parseOptions)
    {
        CompilationWithAnalyzers compilationWithAnalyzers = appCompilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new MsgPack00xMessagePackAnalyzer()));
        ImmutableArray<Diagnostic> analyzerDiagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(analyzerDiagnostics, diagnostic => diagnostic.Id == "AD0001");

        ISourceGenerator generator = new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { generator }, parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(appCompilation, out Compilation outputCompilation, out _);
        GeneratorDriverRunResult result = driver.GetRunResult();

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "CS8785");
        Assert.Contains(result.GeneratedTrees, tree => tree.FilePath.EndsWith("App.TestResolver.g.cs", StringComparison.Ordinal));
        Assert.Empty(outputCompilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    private static async Task<(CSharpCompilation AppCompilation, CSharpParseOptions ParseOptions)> CreateAppCompilationAsync(string librarySource)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var references = new List<MetadataReference>();
        references.AddRange(await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None));
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation libraryCompilation = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(librarySource, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(libraryCompilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        references.Add(libraryCompilation.ToMetadataReference());
        CSharpCompilation appCompilation = CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(appCompilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        return (appCompilation, parseOptions);
    }
}
