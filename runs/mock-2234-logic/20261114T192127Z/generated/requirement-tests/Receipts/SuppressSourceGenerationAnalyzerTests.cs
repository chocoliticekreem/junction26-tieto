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
    private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(string source)
    {
        var frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = frameworkRefs.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
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
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    private static void AssertNoAnalyzerCrash(ImmutableArray<Diagnostic> diagnostics)
    {
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
    }

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
        var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
        AssertNoAnalyzerCrash(diagnostics);
        Assert.Contains(diagnostics, d => d.Id == "MsgPack004");
    }

    [Fact]
    public async Task Suppressed_DuplicateIntKeys_ReportsDiagnosticWithoutCrash()
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
        var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
        AssertNoAnalyzerCrash(diagnostics);
        Assert.Contains(diagnostics, d => d.Id == "MsgPack005");
    }

    [Fact]
    public async Task Suppressed_MixedKeyKinds_ReportsDiagnosticWithoutCrash()
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
        var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
        AssertNoAnalyzerCrash(diagnostics);
        Assert.Contains(diagnostics, d => d.Id.StartsWith("MsgPack", StringComparison.Ordinal) && d.Severity == DiagnosticSeverity.Error);
    }
}
