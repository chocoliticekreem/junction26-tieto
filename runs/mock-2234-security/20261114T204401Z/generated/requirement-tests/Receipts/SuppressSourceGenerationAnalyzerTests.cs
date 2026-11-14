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
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetCustomAttributes(typeof(DiagnosticAnalyzerAttribute), false).Length > 0)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source)
    {
        var framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var refs = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        var compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var analyzers = GetAnalyzers();
        Assert.NotEmpty(analyzers);
        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }

    [Fact]
    public async Task MissingKey_OnSuppressedType_ReportsMsgPack004()
    {
        string source = """
            using MessagePack;

            namespace TestNs
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver { }

                [MessagePackObject(SuppressSourceGeneration = true)]
                public class MyObject
                {
                    public int A { get; set; }
                }
            }
            """;
        var diagnostics = await GetDiagnosticsAsync(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Contains(diagnostics, d => d.Id == "MsgPack004");
    }

    [Fact]
    public async Task DuplicateKeys_OnSuppressedType_ReportsDiagnostic_WithoutAnalyzerCrash()
    {
        string source = """
            using MessagePack;

            namespace TestNs
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver { }

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
        var diagnostics = await GetDiagnosticsAsync(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Contains(diagnostics, d => d.Id.StartsWith("MsgPack", StringComparison.Ordinal) && d.Severity == DiagnosticSeverity.Error);
    }
}
