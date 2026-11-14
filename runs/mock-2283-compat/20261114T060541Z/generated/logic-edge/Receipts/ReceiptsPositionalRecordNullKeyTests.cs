// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ReceiptsPositionalRecordNullKeyTests
{
    private const string ClassSource = """
        using MessagePack;

        [MessagePackObject]
        public class C
        {
            [Key((string)null)]
            public int A { get; set; }
        }
        """;

    private const string RecordSource = """
        using MessagePack;

        [MessagePackObject]
        public record R([property: Key((string)null)] int A);
        """;

    [Fact]
    public async Task NullKeyOnPositionalRecordPropertyIsReportedLikeOnClassProperty()
    {
        ImmutableHashSet<string> classIds = await GetAnalyzerDiagnosticIdsAsync(ClassSource);
        ImmutableHashSet<string> recordIds = await GetAnalyzerDiagnosticIdsAsync(RecordSource);

        Assert.NotEmpty(classIds);
        Assert.All(classIds, id => Assert.Contains(id, recordIds));
    }

    private static async Task<ImmutableHashSet<string>> GetAnalyzerDiagnosticIdsAsync(string source)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        IEnumerable<MetadataReference> references = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        ImmutableArray<DiagnosticAnalyzer> analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new MessagePack.SourceGenerator.Analyzers.MsgPack00xMessagePackAnalyzer());
        ImmutableArray<Diagnostic> diagnostics = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        return diagnostics.Where(d => d.Id.StartsWith("MsgPack", StringComparison.Ordinal)).Select(d => d.Id).ToImmutableHashSet();
    }
}
