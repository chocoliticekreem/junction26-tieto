// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class SuppressSourceGenerationAnalyzerReceiptTests
{
    private const string Source = """
        using MessagePack;

        namespace Sample
        {
            [MessagePackObject]
            public class Control
            {
                public int ControlValue { get; set; }
            }

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class Suppressed
            {
                public int SuppressedValue { get; set; }
            }
        }
        """;

    [Fact]
    public async Task SuppressedTypeStillReportsMsgPack004()
    {
        var framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = new List<MetadataReference>(framework);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        var tree = CSharpSyntaxTree.ParseText(Source, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            "ReceiptsSuppressed",
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzers = typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();
        Assert.NotEmpty(analyzers);

        var diagnostics = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();

        int suppressedStart = Source.IndexOf("SuppressSourceGeneration = true", StringComparison.Ordinal);
        Assert.True(suppressedStart > 0);

        // Sanity: the non-suppressed control type gets MsgPack004, proving the harness runs the analyzer.
        Assert.Contains(diagnostics, d => d.Id == "MsgPack004" && d.Location.IsInSource && d.Location.SourceSpan.Start < suppressedStart);

        // Requirement: with SuppressSourceGeneration = true the analyzer must still report MsgPack004.
        Assert.Contains(diagnostics, d => d.Id == "MsgPack004" && d.Location.IsInSource && d.Location.SourceSpan.Start > suppressedStart);
    }
}
