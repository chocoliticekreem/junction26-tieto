// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ClosedGenericStructFromReferencedAssemblyTests
{
    private const string LibSource = """
        using MessagePack;
        namespace Lib;

        [MessagePackObject]
        public partial record struct Item<T>([property: Key(0)] string Key);
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
        partial class MyResolver { }
        """;

    private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

    [Fact]
    public async Task GeneratorDoesNotThrowAndEmitsResolver()
    {
        CSharpCompilation app = await CreateAppCompilationAsync();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() }, parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(app, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        GeneratorDriverRunResult result = driver.GetRunResult();

        foreach (GeneratorRunResult r in result.Results)
        {
            Assert.True(r.Exception is null, r.Exception?.ToString());
        }

        Assert.DoesNotContain(generatorDiagnostics, d => d.Id == "CS8785");
        Assert.Contains(result.GeneratedTrees, t => t.FilePath.Contains("MyResolver"));

        List<Diagnostic> errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors.Select(e => e.ToString())));
    }

    [Fact]
    public async Task AnalyzerDoesNotThrow()
    {
        CSharpCompilation app = await CreateAppCompilationAsync();

        ImmutableArray<DiagnosticAnalyzer> analyzers = typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetCustomAttributes(typeof(DiagnosticAnalyzerAttribute), false).Length > 0)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();
        Assert.NotEmpty(analyzers);

        List<string> exceptions = new();
        var options = new CompilationWithAnalyzersOptions(
            new Microsoft.CodeAnalysis.Diagnostics.AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
            (Exception ex, DiagnosticAnalyzer analyzer, Diagnostic diagnostic) =>
            {
                lock (exceptions)
                {
                    exceptions.Add(analyzer.GetType().Name + ": " + ex);
                }
            },
            false,
            false);

        CompilationWithAnalyzers withAnalyzers = app.WithAnalyzers(analyzers, options);
        ImmutableArray<Diagnostic> diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync(CancellationToken.None);

        Assert.True(exceptions.Count == 0, string.Join(Environment.NewLine, exceptions));
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
    }

    private static async Task<CSharpCompilation> CreateAppCompilationAsync()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = new(framework);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(LibSource, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using MemoryStream ms = new();
        var emitResult = lib.Emit(ms);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics.Select(d => d.ToString())));

        List<MetadataReference> appReferences = new(references) { MetadataReference.CreateFromImage(ms.ToArray()) };

        return CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource, ParseOptions) },
            appReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
