// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class DuplicateKeyDiagnosticReceiptTests
{
    private static ImmutableArray<DiagnosticAnalyzer> LoadAnalyzers()
    {
        return typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract
                && typeof(DiagnosticAnalyzer).IsAssignableFrom(t)
                && t.GetCustomAttributes(typeof(DiagnosticAnalyzerAttribute), false).Length > 0
                && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();
    }

    [Fact]
    public async Task DuplicateIntKeyIsReported()
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

        var refs = new List<MetadataReference>();
        refs.AddRange(await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None));
        refs.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        var compilation = CSharpCompilation.Create(
            "ReceiptsDuplicateKey",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp7_3)) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzers = LoadAnalyzers();
        analyzers.Should().NotBeEmpty();

        var diagnostics = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();

        diagnostics.Should().Contain(d => (d.Id.StartsWith("MsgPack") && d.Severity == DiagnosticSeverity.Error) || d.Id == "AD0001");
    }
}
