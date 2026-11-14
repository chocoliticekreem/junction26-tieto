// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using MessagePack.SourceGenerator.Analyzers;

namespace Receipts.GeneratedTests;

public class MetadataSymbolDiagnosticLocationTests
{
    [Fact]
    public async Task MetadataMemberWithInvalidKey_DoesNotCrashGeneratorOrAnalyzer()
    {
        const string librarySource = """
            using MessagePack;

            namespace Lib;

            [MessagePackObject]
            public partial struct Item<T>
            {
                [Key((string?)null)]
                public T Value { get; set; }
            }
            """;
        const string appSource = """
            using MessagePack;
            using Lib;

            namespace App;

            [GeneratedMessagePackResolver]
            public partial class TestResolver
            {
            }

            [MessagePackObject]
            public partial class Cmd
            {
                [Key(0)]
                public Item<int> Member { get; set; }
            }
            """;

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
            new[] { CSharpSyntaxTree.ParseText(appSource, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(appCompilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        CompilationWithAnalyzers compilationWithAnalyzers = appCompilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new MsgPack00xMessagePackAnalyzer()));
        ImmutableArray<Diagnostic> analyzerDiagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(analyzerDiagnostics, diagnostic => diagnostic.Id == "AD0001");

        ISourceGenerator generator = new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { generator }, parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(appCompilation, out _, out _);
        GeneratorDriverRunResult result = driver.GetRunResult();
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "CS8785");
    }
}
