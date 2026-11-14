// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class TypeCollectorSuppressionAndDuplicateKeyReceipts
{
    [Fact]
    public async Task SuppressSourceGeneration_StillReportsMsgPack004()
    {
        const string source = """
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class MyObject
            {
                public int Value { get; set; }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(source);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "MsgPack004");
    }

    [Fact]
    public async Task DuplicateKeys_StillReportMsgPack005()
    {
        const string source = """
            using MessagePack;

            [MessagePackObject]
            public class MyObject
            {
                [Key(0)]
                public int First { get; set; }

                [Key(0)]
                public int Second { get; set; }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(source);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "MsgPack005");
    }

    private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(string source)
    {
        ImmutableArray<MetadataReference> references = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        references = references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsCompilation",
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        ImmutableArray<DiagnosticAnalyzer> analyzers = typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
            .Select(type => (DiagnosticAnalyzer)Activator.CreateInstance(type)!)
            .ToImmutableArray();
        Assert.NotEmpty(analyzers);

        return await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
    }
}
