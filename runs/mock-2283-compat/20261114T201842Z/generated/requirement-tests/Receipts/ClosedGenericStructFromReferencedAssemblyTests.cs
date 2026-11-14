// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ClosedGenericStructFromReferencedAssemblyTests
{
    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.CSharp12);

    private const string AppSource = """
        using MessagePack;
        using Lib;

        namespace App
        {
            [MessagePackObject]
            public sealed partial record Cmd([property: Key(0)] string Scope)
            {
                [Key(1)] public Item<int> Member { get; init; }
            }
        }
        """;

    private readonly ITestOutputHelper logger;

    public ClosedGenericStructFromReferencedAssemblyTests(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    [Theory]
    [InlineData("record struct")]
    [InlineData("struct")]
    public async Task GeneratorDoesNotThrowAndEmitsFormatters(string kind)
    {
        CSharpCompilation app = await CreateAppCompilationAsync(kind);

        ISourceGenerator generator = new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { generator }, parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(app, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        GeneratorDriverRunResult run = driver.GetRunResult();

        foreach (Diagnostic d in generatorDiagnostics)
        {
            this.logger.WriteLine(d.ToString());
        }

        foreach (GeneratorRunResult result in run.Results)
        {
            Assert.True(result.Exception is null, result.Exception?.ToString());
        }

        Assert.DoesNotContain(generatorDiagnostics, d => d.Id == "CS8785");
        Assert.NotEmpty(run.GeneratedTrees);
        Assert.Contains(run.GeneratedTrees, t => t.GetText().ToString().Contains("Cmd"));
    }

    [Theory]
    [InlineData("record struct")]
    [InlineData("struct")]
    public async Task AnalyzerDoesNotThrow(string kind)
    {
        CSharpCompilation app = await CreateAppCompilationAsync(kind);

        ImmutableArray<DiagnosticAnalyzer> analyzers = typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetCustomAttribute<DiagnosticAnalyzerAttribute>() is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();
        Assert.NotEmpty(analyzers);

        var exceptions = new ConcurrentBag<Exception>();
        var options = new CompilationWithAnalyzersOptions(
            new Microsoft.CodeAnalysis.Diagnostics.AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
            (ex, analyzer, diag) => exceptions.Add(ex),
            concurrentAnalysis: false,
            logAnalyzerExecutionTime: false);

        ImmutableArray<Diagnostic> diagnostics = await app.WithAnalyzers(analyzers, options).GetAnalyzerDiagnosticsAsync();
        foreach (Diagnostic d in diagnostics)
        {
            this.logger.WriteLine(d.ToString());
        }

        Assert.True(exceptions.IsEmpty, string.Join(Environment.NewLine, exceptions));
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
    }

    private static string GetLibSource(string kind) => kind switch
    {
        "record struct" => """
            using MessagePack;
            namespace Lib
            {
                [MessagePackObject]
                public partial record struct Item<T>([property: Key(0)] string Key);
            }
            """,
        _ => """
            using MessagePack;
            namespace Lib
            {
                [MessagePackObject]
                public partial struct Item<T>
                {
                    [Key(0)]
                    public string Key { get; set; }
                }
            }
            """,
    };

    private static async Task<CSharpCompilation> CreateAppCompilationAsync(string kind)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        ImmutableArray<MetadataReference> baseRefs = framework.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(GetLibSource(kind), ParseOptions) },
            baseRefs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emitResult = lib.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference libReference = MetadataReference.CreateFromImage(stream.ToArray());

        return CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource, ParseOptions) },
            baseRefs.Add(libReference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
