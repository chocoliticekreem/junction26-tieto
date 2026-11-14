// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class MetadataClosedGenericStructReproTests
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    [Fact]
    public async Task ClosedGenericRecordStructFromReference_DoesNotThrow()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        ImmutableArray<MetadataReference> references = framework.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        string libSource = """
            using MessagePack;
            namespace Lib;

            [MessagePackObject]
            public partial record struct Item<T>([property: Key(0)] string Key);
            """;

        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(libSource, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        Microsoft.CodeAnalysis.Emit.EmitResult emitResult = lib.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference libReference = MetadataReference.CreateFromImage(stream.ToArray());

        string appSource = """
            using MessagePack;
            using Lib;
            namespace App;

            [MessagePackObject]
            public sealed partial record Cmd([property: Key(0)] string Scope)
            {
                [Key(1)] public Item<int> Member { get; init; }
            }
            """;

        CSharpCompilation app = CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(appSource, ParseOptions) },
            references.Add(libReference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGenerators(app);
        GeneratorDriverRunResult runResult = driver.GetRunResult();
        Assert.All(runResult.Results, r => Assert.Null(r.Exception));

        List<Exception> analyzerExceptions = new();
        var analyzer = new MessagePack.SourceGenerator.Analyzers.MsgPack00xMessagePackAnalyzer();
        var options = new CompilationWithAnalyzersOptions(
            new Microsoft.CodeAnalysis.Diagnostics.AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
            (ex, a, d) => { lock (analyzerExceptions) { analyzerExceptions.Add(ex); } },
            concurrentAnalysis: false,
            logAnalyzerExecutionTime: false);
        CompilationWithAnalyzers withAnalyzers = app.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer), options);
        ImmutableArray<Diagnostic> diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();

        Assert.Empty(analyzerExceptions);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
    }
}
