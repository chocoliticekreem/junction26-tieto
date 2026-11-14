// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack.SourceGenerator;
using MessagePack.SourceGenerator.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class MetadataPropertyNullKeyTests
{
    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Latest);

    private const string LibSource = """
        using MessagePack;
        namespace Lib;

        [MessagePackObject]
        public partial struct Bad<T>
        {
            [Key((string)null)]
            public string Value { get; set; }
        }
        """;

    private const string AppSource = """
        using MessagePack;
        using Lib;
        namespace App;

        [MessagePackObject]
        public class Holder
        {
            [Key(0)] public Bad<int> Member { get; set; }
        }

        [GeneratedMessagePackResolver]
        partial class AppResolver { }
        """;

    private static async Task<CSharpCompilation> CreateAppCompilationAsync()
    {
        var framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> refs = Enumerable.Concat<MetadataReference>(framework, ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();

        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(LibSource, ParseOptions) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var ms = new MemoryStream();
        var emitResult = lib.Emit(ms);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        refs.Add(MetadataReference.CreateFromImage(ms.ToArray()));
        return CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource, ParseOptions) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    [Fact]
    public async Task Analyzer_DoesNotThrow_WhenReferencedGenericStructPropertyHasNullKey()
    {
        CSharpCompilation app = await CreateAppCompilationAsync();
        var withAnalyzers = app.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new MsgPack00xMessagePackAnalyzer()));
        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
    }

    [Fact]
    public async Task Generator_DoesNotThrow_WhenReferencedGenericStructPropertyHasNullKey()
    {
        CSharpCompilation app = await CreateAppCompilationAsync();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePackGenerator()).WithUpdatedParseOptions(ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(app, out _, out var generatorDiagnostics);
        GeneratorRunResult result = driver.GetRunResult().Results.Single();
        Assert.Null(result.Exception);
        Assert.DoesNotContain(generatorDiagnostics, d => d.Id == "CS8785");
    }
}
