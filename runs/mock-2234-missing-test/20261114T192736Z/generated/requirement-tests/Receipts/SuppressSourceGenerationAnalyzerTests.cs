// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class SuppressSourceGenerationAnalyzerTests(ITestOutputHelper logger)
{
    [Fact]
    public async Task DuplicateIntKeys_SuppressSourceGeneration_ReportsMsgPack005WithoutCrash()
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
    public async Task MixedKeys_SuppressSourceGeneration_ReportsMsgPack005WithoutCrash()
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
        Assert.Contains(diagnostics, d => d.Id == "MsgPack005");
    }

    [Fact]
    public async Task MissingKey_SuppressSourceGeneration_ReportsMsgPack004()
    {
        string source = """
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class MissingKey
            {
                [Key(0)]
                public int A { get; set; }

                public int B { get; set; }
            }
            """;
        var (diagnostics, exceptions) = await AnalyzeAsync(source);
        Assert.Empty(exceptions);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Contains(diagnostics, d => d.Id == "MsgPack004");
    }

    private static ImmutableArray<DiagnosticAnalyzer> LoadAnalyzers()
    {
        return typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract
                && typeof(DiagnosticAnalyzer).IsAssignableFrom(t)
                && t.GetCustomAttributes(typeof(DiagnosticAnalyzerAttribute), false).Length > 0
                && t.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes) is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t, nonPublic: true)!)
            .ToImmutableArray();
    }

    private async Task<(ImmutableArray<Diagnostic> Diagnostics, List<string> Exceptions)> AnalyzeAsync(string source)
    {
        var frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        IEnumerable<MetadataReference> references = frameworkRefs.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        var compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzers = LoadAnalyzers();
        Assert.NotEmpty(analyzers);

        var exceptions = new List<string>();
        var options = new CompilationWithAnalyzersOptions(
            new Microsoft.CodeAnalysis.Diagnostics.AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
            (ex, analyzer, diag) =>
            {
                lock (exceptions)
                {
                    exceptions.Add($"{analyzer.GetType().Name}: {ex}");
                }
            },
            concurrentAnalysis: false,
            logAnalyzerExecutionTime: false);

        var withAnalyzers = compilation.WithAnalyzers(analyzers, options);
        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync(CancellationToken.None);
        foreach (var d in diagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        foreach (var e in exceptions)
        {
            logger.WriteLine(e);
        }

        return (diagnostics, exceptions);
    }
}
