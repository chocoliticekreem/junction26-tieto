// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class SuppressSourceGenerationAnalyzerTests
{
    private static ImmutableArray<DiagnosticAnalyzer> GetAnalyzers()
    {
        return typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly.GetTypes()
            .Where(t => typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsSuppressTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Diagnostic[] compileErrors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(compileErrors.Length == 0, "Test source does not compile: " + string.Join("; ", compileErrors.Select(d => d.ToString())));

        CompilationWithAnalyzers withAnalyzers = compilation.WithAnalyzers(GetAnalyzers());
        return await withAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static string Describe(ImmutableArray<Diagnostic> diagnostics) => string.Join("; ", diagnostics.Select(d => d.ToString()));

    [Fact]
    public async Task SuppressedType_MissingKey_ReportsMsgPack004()
    {
        string source = """
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class MyObject
            {
                public int Value { get; set; }
            }
            """;
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source);
        Assert.False(diagnostics.Any(d => d.Id == "AD0001"), Describe(diagnostics));
        Assert.True(diagnostics.Any(d => d.Id == "MsgPack004"), "Expected MsgPack004. Got: " + Describe(diagnostics));
    }

    [Fact]
    public async Task SuppressedType_DuplicateIntKeys_ReportsDiagnosticWithoutAnalyzerCrash()
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
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source);
        Assert.False(diagnostics.Any(d => d.Id == "AD0001"), "Analyzer threw: " + Describe(diagnostics));
        Assert.True(diagnostics.Any(d => d.Id.StartsWith("MsgPack", StringComparison.Ordinal)), "Expected a MsgPack diagnostic. Got: " + Describe(diagnostics));
    }

    [Fact]
    public async Task SuppressedType_MixedKeyKinds_ReportsDiagnosticWithoutAnalyzerCrash()
    {
        string source = """
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class MyObject
            {
                [Key(0)]
                public int A { get; set; }

                [Key("b")]
                public int B { get; set; }
            }
            """;
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source);
        Assert.False(diagnostics.Any(d => d.Id == "AD0001"), "Analyzer threw: " + Describe(diagnostics));
        Assert.True(diagnostics.Any(d => d.Id.StartsWith("MsgPack", StringComparison.Ordinal)), "Expected a MsgPack diagnostic. Got: " + Describe(diagnostics));
    }
}
