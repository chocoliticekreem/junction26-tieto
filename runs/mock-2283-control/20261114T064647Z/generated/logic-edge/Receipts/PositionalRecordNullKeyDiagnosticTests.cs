// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
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
    public async Task PositionalRecordPropertyWithNullKey_ReportsBothKeysNullDiagnostic()
    {
        string source = """
            using MessagePack;

            namespace App
            {
                [MessagePackObject]
                public partial record Cmd([property: Key((string)null)] string Scope);
            }
            """;

        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = new List<MetadataReference>(framework);
        foreach (MetadataReference r in ReferencesHelper.GetReferences(ReferencesSet.MessagePack))
        {
            references.Add(r);
        }

        CSharpCompilation compilation = CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Assembly asm = typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly;
        Type analyzerType = asm.GetType("MessagePack.SourceGenerator.Analyzers.MsgPack00xMessagePackAnalyzer", throwOnError: true)!;
        DiagnosticAnalyzer analyzer = (DiagnosticAnalyzer)Activator.CreateInstance(analyzerType, nonPublic: true)!;

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        object? descriptorObj = analyzerType.GetField("BothStringAndIntKeyAreNull", flags)?.GetValue(null)
            ?? analyzerType.GetProperty("BothStringAndIntKeyAreNull", flags)?.GetValue(null);
        DiagnosticDescriptor descriptor = Assert.IsType<DiagnosticDescriptor>(descriptorObj);

        CompilationWithAnalyzers withAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create(analyzer));
        ImmutableArray<Diagnostic> diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();
        foreach (Diagnostic d in diagnostics)
        {
            this.logger.WriteLine(d.ToString());
        }

        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Contains(diagnostics, d => d.Id == descriptor.Id && d.Descriptor.MessageFormat.ToString() == descriptor.MessageFormat.ToString());
    }
}
