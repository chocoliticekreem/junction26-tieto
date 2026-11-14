// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
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

        namespace App
        {
            [MessagePackObject]
            public sealed partial record Cmd([property: Key(0)] string Scope)
            {
                [Key(1)] public Item<int> Member { get; init; }
            }

            [GeneratedMessagePackResolver]
            partial class AppResolver
            {
            }
        }
        """;

    private static string LibSource(string kind) => kind == "record struct"
        ? """
            using MessagePack;
            namespace Lib;

            [MessagePackObject]
            public partial record struct Item<T>([property: Key(0)] string Key);
            """
        : """
            using MessagePack;
            namespace Lib;

            [MessagePackObject]
            public partial struct Item<T>
            {
                [Key(0)]
                public string Key { get; set; }
            }
            """;

    [Theory]
    [InlineData("record struct")]
    [InlineData("struct")]
    public async Task Generator_DoesNotThrow_AndEmitsResolver_ForClosedGenericStructFromReference(string kind)
    {
        CSharpCompilation compilation = await CreateAppCompilationAsync(kind);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() }, parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> driverDiagnostics);
        GeneratorDriverRunResult runResult = driver.GetRunResult();

        foreach (Diagnostic d in driverDiagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        GeneratorRunResult result = Assert.Single(runResult.Results);
        Assert.Null(result.Exception);
        Assert.DoesNotContain(driverDiagnostics, d => d.Id == "CS8785");
        Assert.Contains(result.GeneratedSources, s => s.HintName.Contains("AppResolver"));

        Diagnostic[] errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        foreach (Diagnostic e in errors)
        {
            logger.WriteLine(e.ToString());
        }

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("record struct")]
    [InlineData("struct")]
    public async Task Analyzers_DoNotThrow_ForClosedGenericStructFromReference(string kind)
    {
        CSharpCompilation compilation = await CreateAppCompilationAsync(kind);

        ImmutableArray<DiagnosticAnalyzer> analyzers = typeof(global::MessagePack.SourceGenerator.MessagePackGenerator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetCustomAttribute<DiagnosticAnalyzerAttribute>() is not null && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();
        Assert.NotEmpty(analyzers);

        List<Exception> exceptions = new();
        CompilationWithAnalyzersOptions analyzerOptions = new(
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

        ImmutableArray<Diagnostic> diagnostics = await compilation.WithAnalyzers(analyzers, analyzerOptions).GetAnalyzerDiagnosticsAsync(CancellationToken.None);
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
        List<MetadataReference> references = new(framework);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(LibSource(kind), ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using MemoryStream stream = new();
        var emitResult = lib.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics.Select(d => d.ToString())));

        List<MetadataReference> appReferences = new(references)
        {
            MetadataReference.CreateFromImage(stream.ToArray()),
        };

        return CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource, ParseOptions) },
            appReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
