// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class SuppressSourceGenerationAnalyzerTests
{
    private readonly ITestOutputHelper logger;

    public SuppressSourceGenerationAnalyzerTests(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    [Fact]
    public async Task SuppressedType_DuplicateKeys_ReportsDiagnosticWithoutAnalyzerCrash()
    {
        string source = """
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class DuplicateKeys
            {
                [Key(0)]
                public int A { get; set; }

                [Key(0)]
                public int B { get; set; }
            }
            """;

        var (diagnostics, exceptions) = await AnalyzeAsync(source);

        Assert.Empty(exceptions);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Contains(diagnostics, d => d.Id == "MsgPack005");
    }

    [Fact]
    public async Task SuppressedType_MixedIntAndStringKeys_ReportsDiagnosticWithoutAnalyzerCrash()
    {
        string source = """
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class MixedKeys
            {
                [Key(0)]
                public int A { get; set; }

                [Key("b")]
                public int B { get; set; }
            }
            """;

        var (diagnostics, exceptions) = await AnalyzeAsync(source);

        Assert.Empty(exceptions);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Contains(diagnostics, d => d.Id.StartsWith("MsgPack", StringComparison.Ordinal));
    }

    private async Task<(ImmutableArray<Diagnostic> Diagnostics, List<Exception> Exceptions)> AnalyzeAsync(string source)
    {
        var frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = frameworkReferences.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();

        var compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzers = typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetCustomAttribute<DiagnosticAnalyzerAttribute>() is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();
        Assert.NotEmpty(analyzers);

        var exceptions = new List<Exception>();
        var options = new CompilationWithAnalyzersOptions(
            new Microsoft.CodeAnalysis.Diagnostics.AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
            (ex, analyzer, diag) =>
            {
                lock (exceptions)
                {
                    exceptions.Add(ex);
                }
            },
            concurrentAnalysis: false,
            logAnalyzerExecutionTime: false);

        var withAnalyzers = compilation.WithAnalyzers(analyzers, options);
        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();

        foreach (var d in diagnostics)
        {
            this.logger.WriteLine(d.ToString());
        }

        foreach (var ex in exceptions)
        {
            this.logger.WriteLine(ex.ToString());
        }

        return (diagnostics, exceptions);
    }
}
