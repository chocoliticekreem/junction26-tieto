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
    public async Task Suppressed_MissingKey_ReportsMsgPack004()
    {
        string source = """
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class MyObject
            {
                [Key(0)]
                public int A { get; set; }

                public int B { get; set; }
            }
            """;
        ImmutableArray<Microsoft.CodeAnalysis.Diagnostic> diagnostics = await this.GetAnalyzerDiagnosticsAsync(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Contains(diagnostics, d => d.Id == "MsgPack004");
    }

    [Fact]
    public async Task Suppressed_DuplicateKeys_ReportsMsgPack005WithoutAnalyzerCrash()
    {
        string source = """
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class MyObject
            {
                [Key(0)]
                public int A { get; set; }

                [Key(0)]
                public int B { get; set; }
            }
            """;
        ImmutableArray<Microsoft.CodeAnalysis.Diagnostic> diagnostics = await this.GetAnalyzerDiagnosticsAsync(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Contains(diagnostics, d => d.Id == "MsgPack005");
    }

    [Fact]
    public async Task NotSuppressed_MissingKey_StillReportsMsgPack004()
    {
        string source = """
            using MessagePack;

            [MessagePackObject]
            public class MyObject
            {
                [Key(0)]
                public int A { get; set; }

                public int B { get; set; }
            }
            """;
        ImmutableArray<Microsoft.CodeAnalysis.Diagnostic> diagnostics = await this.GetAnalyzerDiagnosticsAsync(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Contains(diagnostics, d => d.Id == "MsgPack004");
    }

    private static ImmutableArray<DiagnosticAnalyzer> GetAnalyzers()
    {
        Assembly assembly = typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly;
        return assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null) is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t, true)!)
            .ToImmutableArray();
    }

    private async Task<ImmutableArray<Microsoft.CodeAnalysis.Diagnostic>> GetAnalyzerDiagnosticsAsync(string source)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = new(framework);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsSuppressTest",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        ImmutableArray<DiagnosticAnalyzer> analyzers = GetAnalyzers();
        Assert.NotEmpty(analyzers);

        ImmutableArray<Microsoft.CodeAnalysis.Diagnostic> diagnostics = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        foreach (Microsoft.CodeAnalysis.Diagnostic d in diagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        return diagnostics;
    }
}
