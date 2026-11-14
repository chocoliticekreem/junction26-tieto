// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack.SourceGenerator;
using MessagePack.SourceGenerator.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ClosedGenericStructFromReferencedAssemblyTests
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
            public string Value { get; set; }
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

        [GeneratedMessagePackResolver]
        internal partial class AppResolver
        {
        }
        """;

    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Latest);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Generator_DoesNotThrow_AndEmitsResolver(bool recordStruct)
    {
        CSharpCompilation app = await CreateAppCompilationAsync(recordStruct ? RecordStructLib : StructLib);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ISourceGenerator[] { new MessagePackGenerator().AsSourceGenerator() }, parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(app, out Compilation _, out ImmutableArray<Diagnostic> diagnostics);
        GeneratorDriverRunResult run = driver.GetRunResult();

        Assert.All(run.Results, r => Assert.Null(r.Exception));
        Assert.DoesNotContain(diagnostics, d => d.Id == "CS8785");
        Assert.Contains(run.Results.SelectMany(r => r.GeneratedSources), s => s.HintName.EndsWith("AppResolver.g.cs", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Analyzer_DoesNotThrow_AndReportsNoWarningsOrErrors(bool recordStruct)
    {
        CSharpCompilation app = await CreateAppCompilationAsync(recordStruct ? RecordStructLib : StructLib);

        CompilationWithAnalyzers withAnalyzers = app.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new MsgPack00xMessagePackAnalyzer()));
        ImmutableArray<Diagnostic> diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();

        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Empty(diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning).Select(d => d.ToString()));
    }

    private static async Task<CSharpCompilation> CreateAppCompilationAsync(string libSource)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = Enumerable.Concat<MetadataReference>(framework, ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();

        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(libSource, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using MemoryStream stream = new MemoryStream();
        var emitResult = lib.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics.Select(d => d.ToString())));

        references.Add(MetadataReference.CreateFromImage(stream.ToArray()));

        return CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
