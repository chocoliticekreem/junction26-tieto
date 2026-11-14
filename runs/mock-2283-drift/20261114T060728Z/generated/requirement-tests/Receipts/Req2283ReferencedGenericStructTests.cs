// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class Req2283ReferencedGenericStructTests(ITestOutputHelper logger)
{
    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Latest);

    private const string RecordStructLib = """
        using MessagePack;
        namespace Lib;

        [MessagePackObject]
        public partial record struct Item<T>([property: Key(0)] string Key);
        """;

    private const string StructLib = """
        using MessagePack;
        namespace Lib;

        [MessagePackObject]
        public struct Item<T>
        {
            [Key(0)]
            public string Key { get; set; }
        }
        """;

    private const string AppSource = """
        using MessagePack; using Lib;
        namespace App;

        [MessagePackObject]
        public sealed partial record Cmd([property: Key(0)] string Scope)
        {
            [Key(1)] public Item<int> Member { get; init; }
        }
        """;

    [Theory]
    [InlineData("recordStruct")]
    [InlineData("struct")]
    public async Task Generator_DoesNotThrow_AndEmitsResolver(string kind)
    {
        CSharpCompilation app = await CreateAppCompilationAsync(kind);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new global::MessagePack.SourceGenerator.MessagePackGenerator()).WithUpdatedParseOptions(ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(app, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        GeneratorDriverRunResult run = driver.GetRunResult();

        foreach (Diagnostic d in generatorDiagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        foreach (GeneratorRunResult result in run.Results)
        {
            Assert.Null(result.Exception);
        }

        Assert.DoesNotContain(generatorDiagnostics, d => d.Id == "CS8785");
        Assert.NotEmpty(run.GeneratedTrees);
        Assert.Contains(run.GeneratedTrees, t => t.ToString().Contains("GeneratedMessagePackResolver"));

        List<Diagnostic> errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        foreach (Diagnostic d in errors)
        {
            logger.WriteLine(d.ToString());
        }

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("recordStruct")]
    [InlineData("struct")]
    public async Task Analyzer_DoesNotThrow(string kind)
    {
        CSharpCompilation app = await CreateAppCompilationAsync(kind);

        var exceptions = new List<Exception>();
        var options = new CompilationWithAnalyzersOptions(
            new Microsoft.CodeAnalysis.Diagnostics.AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
            (ex, analyzer, diag) => exceptions.Add(ex),
            concurrentAnalysis: false,
            logAnalyzerExecutionTime: false);
        CompilationWithAnalyzers withAnalyzers = app.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new global::MessagePack.SourceGenerator.Analyzers.MsgPack00xMessagePackAnalyzer()),
            options);
        ImmutableArray<Diagnostic> diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync(CancellationToken.None);

        foreach (Diagnostic d in diagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        foreach (Exception ex in exceptions)
        {
            logger.WriteLine(ex.ToString());
        }

        Assert.Empty(exceptions);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
    }

    private static async Task<CSharpCompilation> CreateAppCompilationAsync(string kind)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();

        string libSource = kind == "recordStruct" ? RecordStructLib : StructLib;
        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(libSource, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emitResult = lib.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference libReference = MetadataReference.CreateFromImage(stream.ToArray());

        var appReferences = new List<MetadataReference>(references) { libReference };
        return CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource, ParseOptions) },
            appReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
