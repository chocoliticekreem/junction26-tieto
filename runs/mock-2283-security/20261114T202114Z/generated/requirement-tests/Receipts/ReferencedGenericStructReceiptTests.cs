// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack.SourceGenerator;
using MessagePack.SourceGenerator.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ReferencedGenericStructReceiptTests
{
    private const string LibRecordStruct = """
        using MessagePack;
        namespace Lib
        {
            [MessagePackObject]
            public partial record struct Item<T>([property: Key(0)] string Key);
        }
        """;

    private const string LibStruct = """
        using MessagePack;
        namespace Lib
        {
            [MessagePackObject]
            public struct Item<T>
            {
                [Key(0)]
                public string Key { get; set; }
            }
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

            [GeneratedMessagePackResolver]
            partial class MyResolver
            {
            }
        }
        """;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GeneratorDoesNotThrowAndEmitsResolver(bool isRecord)
    {
        CSharpCompilation app = await CreateAppCompilationAsync(isRecord ? LibRecordStruct : LibStruct);
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new MessagePackGenerator().AsSourceGenerator() }, parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(app, out Compilation output, out ImmutableArray<Diagnostic> driverDiagnostics);
        GeneratorDriverRunResult runResult = driver.GetRunResult();

        Assert.All(runResult.Results, r => Assert.Null(r.Exception));
        Assert.DoesNotContain(driverDiagnostics, d => d.Id == "CS8785");
        Assert.Contains(runResult.GeneratedTrees, t => t.GetText().ToString().Contains("MyResolver"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnalyzerDoesNotThrow(bool isRecord)
    {
        CSharpCompilation app = await CreateAppCompilationAsync(isRecord ? LibRecordStruct : LibStruct);
        List<Exception> exceptions = new();
        CompilationWithAnalyzersOptions analyzerOptions = new CompilationWithAnalyzersOptions(null, (ex, analyzer, diagnostic) => exceptions.Add(ex), false, false);
        CompilationWithAnalyzers withAnalyzers = app.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new MsgPack00xMessagePackAnalyzer()), analyzerOptions);
        ImmutableArray<Diagnostic> diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync(CancellationToken.None);

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
        ImmutableArray<MetadataReference> baseRefs = await GetBaseReferencesAsync();
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(libSource, parseOptions) },
            baseRefs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new MemoryStream();
        var emitResult = lib.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics.Select(d => d.ToString())));
        MetadataReference libReference = MetadataReference.CreateFromImage(stream.ToArray());

        return CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource, parseOptions) },
            baseRefs.Add(libReference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
