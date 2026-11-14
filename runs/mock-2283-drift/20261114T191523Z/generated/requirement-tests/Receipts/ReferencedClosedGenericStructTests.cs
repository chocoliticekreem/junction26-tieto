// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using MessagePack.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ReferencedClosedGenericStructTests(ITestOutputHelper logger)
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
        partial class AppResolver { }
        """;

    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.CSharp12);

    [Theory]
    [InlineData("record struct")]
    [InlineData("struct")]
    public async Task Generator_DoesNotThrow_AndEmitsResolver(string kind)
    {
        CSharpCompilation app = await CreateAppCompilationAsync(kind);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new MessagePackGenerator().AsSourceGenerator() }, parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(app, out Compilation output, out ImmutableArray<Diagnostic> driverDiagnostics);
        GeneratorDriverRunResult run = driver.GetRunResult();
        foreach (Diagnostic d in driverDiagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        Assert.All(run.Results, r => Assert.Null(r.Exception));
        Assert.DoesNotContain(driverDiagnostics, d => d.Id == "CS8785");
        Assert.Contains(run.GeneratedTrees, t => t.ToString().Contains("AppResolver"));

        List<Diagnostic> errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        foreach (Diagnostic e in errors)
        {
            logger.WriteLine(e.ToString());
        }

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("record struct")]
    [InlineData("struct")]
    public async Task Analyzer_DoesNotThrow(string kind)
    {
        CSharpCompilation app = await CreateAppCompilationAsync(kind);
        ImmutableArray<DiagnosticAnalyzer> analyzers = typeof(MessagePackGenerator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.IsDefined(typeof(DiagnosticAnalyzerAttribute), false))
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t, true)!)
            .ToImmutableArray();
        Assert.NotEmpty(analyzers);

        var exceptions = new ConcurrentBag<Exception>();
        var options = new CompilationWithAnalyzersOptions(
            new Microsoft.CodeAnalysis.Diagnostics.AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
            (ex, analyzer, diagnostic) => exceptions.Add(ex),
            false,
            false);
        ImmutableArray<Diagnostic> diagnostics = await app.WithAnalyzers(analyzers, options).GetAnalyzerDiagnosticsAsync();
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
        var references = new List<MetadataReference>(framework);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);

        string libSource = kind == "struct" ? StructLib : RecordStructLib;
        CSharpCompilation lib = CSharpCompilation.Create("lib", new[] { CSharpSyntaxTree.ParseText(libSource, ParseOptions) }, references, compilationOptions);
        using var stream = new MemoryStream();
        var emitResult = lib.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        references.Add(MetadataReference.CreateFromImage(stream.ToArray()));

        return CSharpCompilation.Create("app", new[] { CSharpSyntaxTree.ParseText(AppSource, ParseOptions) }, references, compilationOptions);
    }
}
