// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class DuplicateIntKeyFieldTests
{
    private const string PlainSource = """
        using MessagePack;

        namespace TestNs
        {
            [MessagePackObject]
            public class DupFields
            {
                [Key(0)] public int A;
                [Key(0)] public int B;
            }

            [GeneratedMessagePackResolver]
            partial class R { }
        }
        """;

    private const string SuppressedSource = """
        using MessagePack;

        namespace TestNs
        {
            [MessagePackObject(SuppressSourceGeneration = true)]
            public class DupFields
            {
                [Key(0)] public int A;
                [Key(0)] public int B;
            }

            [GeneratedMessagePackResolver]
            partial class R { }
        }
        """;

    [Fact]
    public async Task Analyzer_DoesNotThrow_OnDuplicateIntKeyFields()
    {
        await AssertAnalyzersDoNotThrowAsync(PlainSource);
    }

    [Fact]
    public async Task Analyzer_DoesNotThrow_OnDuplicateIntKeyFields_Suppressed()
    {
        await AssertAnalyzersDoNotThrowAsync(SuppressedSource);
    }

    [Fact]
    public async Task Generator_DoesNotThrow_OnDuplicateIntKeyFields()
    {
        CSharpCompilation compilation = await CreateCompilationAsync(PlainSource);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator());
        driver = driver.RunGenerators(compilation);
        GeneratorDriverRunResult result = driver.GetRunResult();
        foreach (GeneratorRunResult r in result.Results)
        {
            Assert.Null(r.Exception);
        }

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "CS8785");
    }

    private static async Task AssertAnalyzersDoNotThrowAsync(string source)
    {
        CSharpCompilation compilation = await CreateCompilationAsync(source);
        ImmutableArray<DiagnosticAnalyzer> analyzers = typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();
        Assert.NotEmpty(analyzers);

        var exceptions = new List<Exception>();
        var options = new CompilationWithAnalyzersOptions(
            new Microsoft.CodeAnalysis.Diagnostics.AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
            (ex, analyzer, diag) => { lock (exceptions) { exceptions.Add(ex); } },
            concurrentAnalysis: false,
            logAnalyzerExecutionTime: false);
        CompilationWithAnalyzers cwa = compilation.WithAnalyzers(analyzers, options);
        ImmutableArray<Diagnostic> diagnostics = await cwa.GetAnalyzerDiagnosticsAsync();

        Assert.Empty(exceptions);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
    }

    private static async Task<CSharpCompilation> CreateCompilationAsync(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, parseOptions);
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        IEnumerable<MetadataReference> refs = framework.Concat<MetadataReference>(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        return CSharpCompilation.Create(
            "ReceiptsDupKeyAssembly",
            new[] { tree },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
