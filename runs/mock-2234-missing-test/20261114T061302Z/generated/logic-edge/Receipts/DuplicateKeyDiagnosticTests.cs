// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class DuplicateKeyDiagnosticTests(ITestOutputHelper logger)
{
    private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(string source)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        var compilation = CSharpCompilation.Create(
            "DupKeyAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzers = typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetCustomAttributes(typeof(DiagnosticAnalyzerAttribute), false).Length > 0)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();

        return await compilation.WithAnalyzers(analyzers).GetAllDiagnosticsAsync();
    }

    [Fact]
    public async Task DuplicateIntKey_IsReported()
    {
        string source = """
            using MessagePack;

            [MessagePackObject]
            public class Dup
            {
                [Key(0)]
                public int A { get; set; }

                [Key(0)]
                public int B { get; set; }
            }
            """;
        var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
        foreach (var d in diagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        diagnostics.Should().Contain(d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("MsgPack", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DuplicateIntKey_SuppressSourceGeneration_IsReported()
    {
        string source = """
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class Dup
            {
                [Key(0)]
                public int A { get; set; }

                [Key(0)]
                public int B { get; set; }
            }
            """;
        var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
        foreach (var d in diagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        diagnostics.Should().Contain(d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("MsgPack", StringComparison.Ordinal));
    }
}
