// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ClosedGenericStructFromMetadataTests
{
    private const string AppSource = """
        using MessagePack;
        using Lib;
        namespace App
        {
            [MessagePackObject]
            public sealed partial record Cmd([property: Key(0)] string Scope)
            {
                [Key(1)] public Item<int> Member { get; init; }
            }
        }
        """;

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    public static IEnumerable<object[]> LibSources => new[]
    {
        new object[] { "using MessagePack; namespace Lib { [MessagePackObject] public partial record struct Item<T>([property: Key(0)] string Key); }" },
        new object[] { "using MessagePack; namespace Lib { [MessagePackObject] public struct Item<T> { [Key(0)] public string Key { get; set; } } }" },
    };

    [Theory]
    [MemberData(nameof(LibSources))]
    public async Task Generator_DoesNotThrow_OnClosedGenericStructFromMetadata(string libSource)
    {
        CSharpCompilation app = await CreateAppCompilationAsync(libSource);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() }, parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(app, out _, out ImmutableArray<Diagnostic> diagnostics);
        GeneratorDriverRunResult result = driver.GetRunResult();
        Assert.All(result.Results, r => Assert.Null(r.Exception));
        Assert.DoesNotContain(diagnostics, d => d.Id == "CS8785");
        Assert.NotEmpty(result.GeneratedTrees);
    }

    [Theory]
    [MemberData(nameof(LibSources))]
    public async Task Analyzer_DoesNotThrow_OnClosedGenericStructFromMetadata(string libSource)
    {
        CSharpCompilation app = await CreateAppCompilationAsync(libSource);
        List<Exception> exceptions = new();
        var options = new CompilationWithAnalyzersOptions(
            new Microsoft.CodeAnalysis.Diagnostics.AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
            (ex, analyzer, diag) => { lock (exceptions) { exceptions.Add(ex); } },
            concurrentAnalysis: false,
            logAnalyzerExecutionTime: false);
        CompilationWithAnalyzers withAnalyzers = app.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new MessagePack.SourceGenerator.Analyzers.MsgPack00xMessagePackAnalyzer()), options);
        ImmutableArray<Diagnostic> diagnostics = await withAnalyzers.GetAllDiagnosticsAsync();
        Assert.Empty(exceptions);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
    }

    private static async Task<CSharpCompilation> CreateAppCompilationAsync(string libSource)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = framework.Concat<MetadataReference>(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        CSharpCompilation lib = CSharpCompilation.Create("Lib", new[] { CSharpSyntaxTree.ParseText(libSource, ParseOptions) }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        var emitResult = lib.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        references.Add(MetadataReference.CreateFromImage(stream.ToArray()));
        return CSharpCompilation.Create("App", new[] { CSharpSyntaxTree.ParseText(AppSource, ParseOptions) }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
