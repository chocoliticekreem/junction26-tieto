// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class Req2283ReferencedGenericStructTests
{
    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.CSharp10);

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

        namespace App
        {
            [MessagePackObject]
            public sealed class Cmd
            {
                [Key(0)]
                public string Scope { get; set; }

                [Key(1)]
                public Item<int> Member { get; set; }
            }
        }
        """;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Generator_DoesNotThrow_AndEmitsResolver(bool recordStruct)
    {
        CSharpCompilation app = await CreateAppCompilationAsync(recordStruct ? RecordStructLib : StructLib);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(app, out Compilation _, out ImmutableArray<Diagnostic> driverDiagnostics);
        GeneratorDriverRunResult result = driver.GetRunResult();

        Assert.All(result.Results, r => Assert.Null(r.Exception));
        Assert.DoesNotContain(driverDiagnostics, d => d.Id == "CS8785");
        Assert.Contains(result.GeneratedTrees, t => t.ToString().Contains("GeneratedMessagePackResolver"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Analyzer_DoesNotThrow(bool recordStruct)
    {
        CSharpCompilation app = await CreateAppCompilationAsync(recordStruct ? RecordStructLib : StructLib);

        var exceptions = new List<Exception>();
        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new global::MessagePack.SourceGenerator.Analyzers.MsgPack00xMessagePackAnalyzer());
        var withAnalyzersOptions = new CompilationWithAnalyzersOptions(
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

        CompilationWithAnalyzers withAnalyzers = app.WithAnalyzers(analyzers, withAnalyzersOptions);
        ImmutableArray<Diagnostic> diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();

        Assert.Empty(exceptions);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
    }

    private static async Task<ImmutableArray<MetadataReference>> GetBaseReferencesAsync()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        return framework.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
    }

    private static async Task<CSharpCompilation> CreateAppCompilationAsync(string libSource)
    {
        ImmutableArray<MetadataReference> references = await GetBaseReferencesAsync();

        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(libSource, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emitResult = lib.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics.Select(d => d.ToString())));
        MetadataReference libReference = MetadataReference.CreateFromImage(stream.ToArray());

        return CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource, ParseOptions) },
            references.Add(libReference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
