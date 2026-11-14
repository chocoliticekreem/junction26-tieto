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
    private static ImmutableArray<DiagnosticAnalyzer> LoadAnalyzers()
    {
        Assembly asm = typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly;
        return asm.GetTypes()
            .Where(t => !t.IsAbstract
                && typeof(DiagnosticAnalyzer).IsAssignableFrom(t)
                && t.GetCustomAttributes(typeof(DiagnosticAnalyzerAttribute), false).Length > 0
                && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        ImmutableArray<MetadataReference> frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        IEnumerable<MetadataReference> allRefs = frameworkRefs.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp7_3)) },
            allRefs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        ImmutableArray<DiagnosticAnalyzer> analyzers = LoadAnalyzers();
        Assert.NotEmpty(analyzers);
        CompilationWithAnalyzers withAnalyzers = compilation.WithAnalyzers(analyzers);
        return await withAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static void AssertNoAnalyzerCrash(ImmutableArray<Diagnostic> diagnostics)
    {
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
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
        ImmutableArray<Diagnostic> diagnostics = await AnalyzeAsync(source);
        AssertNoAnalyzerCrash(diagnostics);
        Assert.Contains(diagnostics, d => d.Id == "MsgPack004");
    }

    [Fact]
    public async Task SuppressedType_DuplicateKey_ReportsDiagnosticWithoutCrash()
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
        AssertNoAnalyzerCrash(diagnostics);
        Assert.Contains(diagnostics, d => d.Id.StartsWith("MsgPack", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SuppressedType_MixedKeyKinds_ReportsDiagnosticWithoutCrash()
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
        AssertNoAnalyzerCrash(diagnostics);
        Assert.Contains(diagnostics, d => d.Id.StartsWith("MsgPack", StringComparison.Ordinal));
    }
}
