// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class Req2283ClosedGenericStructFromReferenceTests
{
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
        public partial struct Item<T>
        {
            [Key(0)]
            public string Key { get; set; }
        }
        """;

    private const string AppSource = """
        using MessagePack;
        using Lib;
        namespace App;

        [MessagePackObject]
        public sealed partial record Cmd([property: Key(0)] string Scope)
        {
            [Key(1)] public Item<int> Member { get; init; }
        }
        """;

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp12);

    [Theory]
    [InlineData(RecordStructLib)]
    [InlineData(StructLib)]
    public async Task Generator_DoesNotThrow_AndEmitsResolver(string libSource)
    {
        CSharpCompilation app = await CreateAppCompilationAsync(libSource);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new MessagePackGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(app, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        GeneratorDriverRunResult result = driver.GetRunResult();

        Assert.All(result.Results, r => Assert.Null(r.Exception));
        Assert.DoesNotContain(generatorDiagnostics, d => d.Id == "CS8785");
        Assert.Contains(result.GeneratedTrees, t => t.ToString().Contains("GeneratedMessagePackResolver"));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Theory]
    [InlineData(RecordStructLib)]
    [InlineData(StructLib)]
    public async Task Analyzers_DoNotThrow(string libSource)
    {
        CSharpCompilation app = await CreateAppCompilationAsync(libSource);
        ImmutableArray<DiagnosticAnalyzer> analyzers = typeof(MessagePackGenerator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetCustomAttributes(typeof(DiagnosticAnalyzerAttribute), false).Length > 0)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t, nonPublic: true)!)
            .ToImmutableArray();
        Assert.NotEmpty(analyzers);

        List<Exception> exceptions = [];
        var options = new CompilationWithAnalyzersOptions(
            new Microsoft.CodeAnalysis.Diagnostics.AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
            (ex, analyzer, diagnostic) => { lock (exceptions) { exceptions.Add(ex); } },
            concurrentAnalysis: false,
            logAnalyzerExecutionTime: false);
        ImmutableArray<Diagnostic> diagnostics = await app.WithAnalyzers(analyzers, options).GetAnalyzerDiagnosticsAsync();

        Assert.Empty(exceptions);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
    }

    private static async Task<CSharpCompilation> CreateAppCompilationAsync(string libSource)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = [.. framework, .. ReferencesHelper.GetReferences(ReferencesSet.MessagePack)];

        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            [CSharpSyntaxTree.ParseText(libSource, ParseOptions)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        var emitResult = lib.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference libReference = MetadataReference.CreateFromImage(stream.ToArray());

        List<MetadataReference> appReferences = [.. references, libReference];
        return CSharpCompilation.Create(
            "App",
            [CSharpSyntaxTree.ParseText(AppSource, ParseOptions)],
            appReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
