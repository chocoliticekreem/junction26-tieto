// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
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

        [GeneratedMessagePackResolver]
        internal partial class AppResolver
        {
        }
        """;

    [Theory]
    [InlineData(RecordStructLib)]
    [InlineData(StructLib)]
    public async Task Generator_DoesNotThrow_AndEmitsResolver(string libSource)
    {
        var (app, parseOptions) = await CreateAppCompilationAsync(libSource);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(app, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        GeneratorDriverRunResult result = driver.GetRunResult();

        Assert.All(result.Results, r => Assert.Null(r.Exception));
        Assert.DoesNotContain(generatorDiagnostics, d => d.Id == "CS8785");
        Assert.Contains(result.GeneratedTrees, t => t.ToString().Contains("AppResolver"));

        List<Diagnostic> errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    [Theory]
    [InlineData(RecordStructLib)]
    [InlineData(StructLib)]
    public async Task Analyzer_DoesNotThrow(string libSource)
    {
        var (app, _) = await CreateAppCompilationAsync(libSource);

        var exceptions = new ConcurrentBag<Exception>();
        var withAnalyzersOptions = new CompilationWithAnalyzersOptions(
            new Microsoft.CodeAnalysis.Diagnostics.AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
            (ex, analyzer, diagnostic) => exceptions.Add(ex),
            false,
            false);
        ImmutableArray<DiagnosticAnalyzer> analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new global::MessagePack.SourceGenerator.Analyzers.MsgPack00xMessagePackAnalyzer());

        ImmutableArray<Diagnostic> diagnostics = await app.WithAnalyzers(analyzers, withAnalyzersOptions).GetAnalyzerDiagnosticsAsync();

        Assert.True(exceptions.IsEmpty, string.Join(Environment.NewLine, exceptions));
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
    }

    private static async Task<(CSharpCompilation App, CSharpParseOptions ParseOptions)> CreateAppCompilationAsync(string libSource)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var references = new List<MetadataReference>();
        references.AddRange(await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None));
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);

        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(libSource, parseOptions) },
            references,
            compilationOptions);
        using var stream = new MemoryStream();
        var emitResult = lib.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        // Reference the library as metadata, as a ProjectReference build does.
        references.Add(MetadataReference.CreateFromImage(stream.ToArray()));

        CSharpCompilation app = CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource, parseOptions) },
            references,
            compilationOptions);
        return (app, parseOptions);
    }
}
