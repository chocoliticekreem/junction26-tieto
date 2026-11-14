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
    private readonly ITestOutputHelper logger;

    public PositionalRecordNullKeyDiagnosticTests(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    [Fact]
    public async Task NullStringKeyOnPositionalRecordPropertyIsReported()
    {
        string source = """
            using MessagePack;

            namespace App
            {
                [MessagePackObject]
                public partial record Cmd([property: Key((string)null)] string Scope);
            }
            """;

        ImmutableArray<MetadataReference> frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        IEnumerable<MetadataReference> references = frameworkRefs.Cast<MetadataReference>()
            .Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack).Cast<MetadataReference>());

        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp9));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "App",
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        ImmutableArray<DiagnosticAnalyzer> analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new MsgPack00xMessagePackAnalyzer());
        ImmutableArray<Diagnostic> diagnostics = await compilation.WithAnalyzers(analyzers).GetAllDiagnosticsAsync();

        foreach (Diagnostic d in diagnostics)
        {
            this.logger.WriteLine(d.ToString());
        }

        diagnostics.Should().NotContain(d => d.Id == "AD0001");

        int scopeStart = source.IndexOf("Scope", StringComparison.Ordinal);
        diagnostics.Should().Contain(
            d => d.Id.StartsWith("MsgPack", StringComparison.Ordinal)
                && d.Location.IsInSource
                && d.Location.SourceSpan.Start <= scopeStart
                && d.Location.SourceSpan.End >= scopeStart,
            "a [Key] with neither an int nor a string value on a positional record property in source must still be reported");
    }
}
