// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
    public async Task SuppressedType_MemberWithoutKey_ReportsMsgPack004()
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

        ImmutableArray<Diagnostic> diagnostics = await this.GetAnalyzerDiagnosticsAsync(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Contains(diagnostics, d => d.Id == "MsgPack004");
    }

    [Fact]
    public async Task SuppressedType_DuplicateKey_ReportsMsgPack005()
    {
        string source = """
            using MessagePack;

            namespace TestNs
            {
                [MessagePackObject(SuppressSourceGeneration = true)]
                public class MyObject2
                {
                    [Key(0)]
                    public int A { get; set; }

                    [Key(0)]
                    public int B { get; set; }
                }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await this.GetAnalyzerDiagnosticsAsync(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Contains(diagnostics, d => d.Id == "MsgPack005");
    }

    private async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(string source)
    {
        var references = new List<MetadataReference>();
        references.AddRange(await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None));
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        ImmutableArray<DiagnosticAnalyzer> analyzers = typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly
            .GetTypes()
            .Where(t => typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();

        Assert.NotEmpty(analyzers);

        ImmutableArray<Diagnostic> diagnostics = await compilation.WithAnalyzers(analyzers).GetAllDiagnosticsAsync();
        foreach (Diagnostic d in diagnostics)
        {
            this.logger.WriteLine(d.ToString());
        }

        return diagnostics;
    }
}
