// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
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
    public async Task MsgPack004ReportedWhenSourceGenerationSuppressed()
    {
        string source = """
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class SuppressedObject
            {
                [Key(0)]
                public int A { get; set; }

                public int B { get; set; }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAnalyzersAsync(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Contains(diagnostics, d => d.Id == "MsgPack004");
    }

    [Fact]
    public async Task MsgPack005ReportedWhenSourceGenerationSuppressed()
    {
        string source = """
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class SuppressedDuplicateKeys
            {
                [Key(0)]
                public int A { get; set; }

                [Key(0)]
                public int B { get; set; }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await RunAnalyzersAsync(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Contains(diagnostics, d => d.Id == "MsgPack005");
    }

    private async Task<ImmutableArray<Diagnostic>> RunAnalyzersAsync(string source)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = new List<MetadataReference>(framework);
        foreach (MetadataReference r in ReferencesHelper.GetReferences(ReferencesSet.MessagePack))
        {
            references.Add(r);
        }

        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsAssembly",
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Diagnostic[] compileErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        foreach (Diagnostic d in compileErrors)
        {
            this.logger.WriteLine("compile: " + d.ToString());
        }

        Assert.Empty(compileErrors);

        Assembly analyzerAssembly = typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly;
        ImmutableArray<DiagnosticAnalyzer> analyzers = analyzerAssembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetCustomAttributes(typeof(DiagnosticAnalyzerAttribute), false).Length > 0 && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();
        Assert.NotEmpty(analyzers);

        ImmutableArray<Diagnostic> diagnostics = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        foreach (Diagnostic d in diagnostics)
        {
            this.logger.WriteLine(d.ToString());
        }

        return diagnostics;
    }
}
