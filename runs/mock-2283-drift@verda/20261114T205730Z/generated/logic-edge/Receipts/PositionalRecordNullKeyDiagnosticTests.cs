// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack.SourceGenerator.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class PositionalRecordNullKeyDiagnosticTests
{
    [Fact]
    public async Task PositionalRecordPropertyWithNullKeyStillGetsMsgPack005()
    {
        const string source = """
            using MessagePack;

            [MessagePackObject]
            public partial record MyRecord([property: Key(null)] string Value);
            """;

        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Latest));
        ImmutableArray<MetadataReference> references = await ReferenceAssemblies.Net.Net80.ResolveAsync(
            LanguageNames.CSharp,
            CancellationToken.None);
        references = references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "PositionalRecordNullKey",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CompilationWithAnalyzers compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new MsgPack00xMessagePackAnalyzer()));
        ImmutableArray<Diagnostic> diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "MsgPack005");
    }
}
