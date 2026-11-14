// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class SuppressSourceGenerationAnalyzerReceiptTests
{
    private readonly ITestOutputHelper logger;

    public SuppressSourceGenerationAnalyzerReceiptTests(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    [Fact]
    public async Task SuppressedType_MissingKey_ReportsMsgPack004()
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
        ImmutableArray<Diagnostic> diagnostics = await this.GetAnalyzerDiagnosticsAsync(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Contains(diagnostics, d => d.Id == "MsgPack004");
    }

    [Fact]
    public async Task SuppressedType_MixedKeys_ReportsMsgPack005WithoutCrash()
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
        ImmutableArray<Diagnostic> diagnostics = await this.GetAnalyzerDiagnosticsAsync(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Contains(diagnostics, d => d.Id == "MsgPack005");
    }

    [Fact]
    public async Task SuppressedType_DuplicateKeys_ReportsMsgPack005WithoutCrash()
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
        ImmutableArray<Diagnostic> diagnostics = await this.GetAnalyzerDiagnosticsAsync(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Contains(diagnostics, d => d.Id == "MsgPack005");
    }

    private static ImmutableArray<DiagnosticAnalyzer> GetAnalyzers()
    {
        return typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetCustomAttributes(typeof(DiagnosticAnalyzerAttribute), false).Length > 0)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();
    }

    private async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(string source)
    {
        ImmutableArray<MetadataReference> frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        IEnumerable<MetadataReference> references = frameworkReferences.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack).Cast<MetadataReference>());
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsSuppressTest",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        ImmutableArray<DiagnosticAnalyzer> analyzers = GetAnalyzers();
        Assert.NotEmpty(analyzers);
        ImmutableArray<Diagnostic> diagnostics = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        foreach (Diagnostic d in diagnostics)
        {
            this.logger.WriteLine(d.ToString());
        }

        return diagnostics;
    }
}
