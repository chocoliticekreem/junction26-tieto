// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class SuppressSourceGenerationAnalyzerReceipts
{
    private readonly ITestOutputHelper logger;

    public SuppressSourceGenerationAnalyzerReceipts(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    [Fact]
    public async Task SuppressedType_MissingKey_ReportsMsgPack004()
    {
        string source = """
            using MessagePack;

            namespace TestNs
            {
                [MessagePackObject(SuppressSourceGeneration = true)]
                public class MyObject
                {
                    [Key(0)]
                    public int A { get; set; }

                    public int B { get; set; }
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await this.GetDiagnosticsAsync(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Contains(diagnostics, d => d.Id == "MsgPack004");
    }

    [Fact]
    public async Task SuppressedType_DuplicateKeys_ReportsDiagnostic()
    {
        string source = """
            using MessagePack;

            namespace TestNs
            {
                [MessagePackObject(SuppressSourceGeneration = true)]
                public class MyObject
                {
                    [Key(0)]
                    public int A { get; set; }

                    [Key(0)]
                    public int B { get; set; }
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await this.GetDiagnosticsAsync(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Contains(diagnostics, d => d.Id.StartsWith("MsgPack", StringComparison.Ordinal));
    }

    private async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source)
    {
        ImmutableArray<MetadataReference> frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = new List<MetadataReference>(frameworkRefs);
        foreach (MetadataReference r in ReferencesHelper.GetReferences(ReferencesSet.MessagePack))
        {
            references.Add(r);
        }

        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        ImmutableArray<DiagnosticAnalyzer> analyzers = typeof(global::MessagePack.SourceGenerator.MessagePackGenerator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();
        Assert.NotEmpty(analyzers);

        CompilationWithAnalyzers withAnalyzers = compilation.WithAnalyzers(analyzers);
        ImmutableArray<Diagnostic> diagnostics = await withAnalyzers.GetAllDiagnosticsAsync();
        foreach (Diagnostic d in diagnostics)
        {
            this.logger.WriteLine(d.ToString());
        }

        return diagnostics;
    }
}
