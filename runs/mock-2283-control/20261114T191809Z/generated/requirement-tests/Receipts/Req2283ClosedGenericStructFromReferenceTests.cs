// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class Req2283ClosedGenericStructFromReferenceTests(ITestOutputHelper logger)
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

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

    [Theory]
    [InlineData("record struct")]
    [InlineData("struct")]
    [InlineData("class")]
    public async Task Generator_DoesNotThrow_AndEmitsResolver(string itemKind)
    {
        CSharpCompilation app = await CreateAppCompilationAsync(itemKind);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() }, parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(app, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        GeneratorDriverRunResult result = driver.GetRunResult();

        foreach (Diagnostic d in generatorDiagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        Assert.Empty(result.Results.Where(r => r.Exception is not null).Select(r => r.Exception!.ToString()));
        Assert.DoesNotContain(generatorDiagnostics, d => d.Id == "CS8785");
        Assert.Contains(result.GeneratedTrees, t => t.ToString().Contains("GeneratedMessagePackResolver"));

        List<Diagnostic> errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        foreach (Diagnostic d in errors)
        {
            logger.WriteLine(d.ToString());
        }

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("record struct")]
    [InlineData("struct")]
    [InlineData("class")]
    public async Task Analyzer_DoesNotThrow(string itemKind)
    {
        CSharpCompilation app = await CreateAppCompilationAsync(itemKind);

        List<Exception> exceptions = new();
        CompilationWithAnalyzersOptions options = new(
            new Microsoft.CodeAnalysis.Diagnostics.AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
            (ex, analyzer, diagnostic) => exceptions.Add(ex),
            concurrentAnalysis: false,
            logAnalyzerExecutionTime: false);
        CompilationWithAnalyzers withAnalyzers = app.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new MessagePack.SourceGenerator.Analyzers.MsgPack00xMessagePackAnalyzer()), options);
        ImmutableArray<Diagnostic> diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();

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

    private static string GetLibSource(string kind) => kind switch
    {
        "record struct" => """
            using MessagePack;
            namespace Lib;

            [MessagePackObject]
            public partial record struct Item<T>([property: Key(0)] string Key);
            """,
        "struct" => """
            using MessagePack;
            namespace Lib;

            [MessagePackObject]
            public partial struct Item<T>
            {
                [Key(0)]
                public string Key { get; set; }
            }
            """,
        "class" => """
            using MessagePack;
            namespace Lib;

            [MessagePackObject]
            public partial class Item<T>
            {
                [Key(0)]
                public string Key { get; set; }
            }
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static async Task<CSharpCompilation> CreateAppCompilationAsync(string kind)
    {
        ImmutableArray<MetadataReference> references = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        references = references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(GetLibSource(kind), ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using MemoryStream stream = new();
        var emitResult = lib.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        // A PE image reference makes Item<T> a true metadata symbol (no DeclaringSyntaxReferences), like a ProjectReference.
        MetadataReference libReference = MetadataReference.CreateFromImage(stream.ToArray());

        return CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource, ParseOptions) },
            references.Add(libReference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
