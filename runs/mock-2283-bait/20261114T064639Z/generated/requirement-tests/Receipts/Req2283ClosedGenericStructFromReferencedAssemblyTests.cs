// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack.SourceGenerator;
using MessagePack.SourceGenerator.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class Req2283ClosedGenericStructFromReferencedAssemblyTests
{
    private const string LibRecordStruct = """
        using MessagePack;
        namespace Lib;

        [MessagePackObject]
        public partial record struct Item<T>([property: Key(0)] string Key);
        """;

    private const string LibPlainStruct = """
        using MessagePack;
        namespace Lib;

        [MessagePackObject]
        public partial struct Item<T>
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

    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Latest);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Generator_DoesNotThrow_AndEmitsResolver(bool recordStruct)
    {
        List<MetadataReference> references = await GetBaseReferencesAsync();
        MetadataReference libRef = BuildLib(recordStruct ? LibRecordStruct : LibPlainStruct, references);
        CSharpCompilation app = CreateApp(references, libRef);

        GeneratorDriverRunResult result = RunGenerator(app, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);

        Assert.All(result.Results, r => Assert.Null(r.Exception));
        Assert.DoesNotContain(generatorDiagnostics, d => d.Id == "CS8785");
        Assert.Contains(result.GeneratedTrees, t => t.ToString().Contains("GeneratedMessagePackResolver"));

        List<Diagnostic> errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Analyzer_DoesNotThrow(bool recordStruct)
    {
        List<MetadataReference> references = await GetBaseReferencesAsync();
        MetadataReference libRef = BuildLib(recordStruct ? LibRecordStruct : LibPlainStruct, references);
        CSharpCompilation app = CreateApp(references, libRef);

        List<Exception> exceptions = new();
        CompilationWithAnalyzersOptions options = new CompilationWithAnalyzersOptions(
            new Microsoft.CodeAnalysis.Diagnostics.AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
            (ex, analyzer, diagnostic) =>
            {
                lock (exceptions)
                {
                    exceptions.Add(ex);
                }
            },
            concurrentAnalysis: false,
            logAnalyzerExecutionTime: false);

        ImmutableArray<DiagnosticAnalyzer> analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new MsgPack00xMessagePackAnalyzer());
        ImmutableArray<Diagnostic> diagnostics = await app.WithAnalyzers(analyzers, options).GetAnalyzerDiagnosticsAsync();

        Assert.True(exceptions.Count == 0, string.Join(Environment.NewLine, exceptions));
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
    }

    private static async Task<List<MetadataReference>> GetBaseReferencesAsync()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> list = new List<MetadataReference>(framework);
        list.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        return list;
    }

    private static MetadataReference BuildLib(string source, List<MetadataReference> references)
    {
        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        RunGenerator(lib, out Compilation libWithGenerated, out _);
        using MemoryStream stream = new MemoryStream();
        var emitResult = libWithGenerated.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static CSharpCompilation CreateApp(List<MetadataReference> references, MetadataReference libRef)
    {
        List<MetadataReference> appReferences = new List<MetadataReference>(references) { libRef };
        return CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource, ParseOptions) },
            appReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static GeneratorDriverRunResult RunGenerator(Compilation compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePackGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out output, out diagnostics);
        return driver.GetRunResult();
    }
}
