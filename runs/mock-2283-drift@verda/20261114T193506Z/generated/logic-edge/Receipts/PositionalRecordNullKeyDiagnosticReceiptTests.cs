// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using MessagePack.SourceGenerator.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class PositionalRecordNullKeyDiagnosticReceiptTests
{
    [Fact]
    public async Task NullKeyOnPositionalRecordPropertyIsStillDiagnosed()
    {
        const string source = """
            using MessagePack;

            [MessagePackObject]
            public partial record R([property: Key(null)] string Value);
            """;

        ImmutableArray<MetadataReference> references = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        references = references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsApp",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new MsgPack00xMessagePackAnalyzer();
        ImmutableArray<Diagnostic> diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer))
            .GetAnalyzerDiagnosticsAsync();

        diagnostics.Should().Contain(d => d.Id == MsgPack00xMessagePackAnalyzer.BothStringAndIntKeyAreNull.Id);
    }
}
