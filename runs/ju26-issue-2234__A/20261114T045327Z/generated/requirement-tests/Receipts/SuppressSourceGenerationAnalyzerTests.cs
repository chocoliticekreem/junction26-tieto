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
    private static ImmutableArray<DiagnosticAnalyzer> LoadAnalyzers()
    {
        Assembly asm = typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly;
        return asm.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();
    }

    private async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        ImmutableArray<MetadataReference> references = (await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None))
            .AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        ImmutableArray<DiagnosticAnalyzer> analyzers = LoadAnalyzers();
        Assert.NotEmpty(analyzers);
        ImmutableArray<Diagnostic> diagnostics = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        foreach (Diagnostic d in diagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        return diagnostics;
    }

    [Fact]
    public async Task SuppressedType_MissingKey_ReportsMsgPack004()
    {
        string source = """
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class SuppressedMissingKey
            {
                [Key(0)]
                public int A { get; set; }

                public int B { get; set; }
            }
            """;
        ImmutableArray<Diagnostic> diagnostics = await this.AnalyzeAsync(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Contains(diagnostics, d => d.Id == "MsgPack004");
    }

    [Fact]
    public async Task SuppressedType_DuplicateKeys_ReportsDiagnosticWithoutCrash()
    {
        string source = """
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class SuppressedDuplicateKey
            {
                [Key(0)]
                public int A { get; set; }

                [Key(0)]
                public int B { get; set; }
            }
            """;
        ImmutableArray<Diagnostic> diagnostics = await this.AnalyzeAsync(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Contains(diagnostics, d => d.Id.StartsWith("MsgPack", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SuppressedType_MixedKeys_ReportsDiagnosticWithoutCrash()
    {
        string source = """
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class SuppressedMixedKeys
            {
                [Key(0)]
                public int A { get; set; }

                [Key("b")]
                public int B { get; set; }
            }
            """;
        ImmutableArray<Diagnostic> diagnostics = await this.AnalyzeAsync(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Contains(diagnostics, d => d.Id.StartsWith("MsgPack", StringComparison.Ordinal));
    }
}
