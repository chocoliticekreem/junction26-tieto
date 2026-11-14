// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using MessagePack.SourceGenerator.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class MetadataGenericStructNullKeyAnalyzerTests
{
    private const string LibSource = """
        using MessagePack;
        namespace Lib
        {
            [MessagePackObject]
            public struct Item<T>
            {
                [Key((string)null)]
                public string Key { get; set; }
            }
        }
        """;

    private const string AppSource = """
        using MessagePack;
        using Lib;
        namespace App
        {
            [MessagePackObject]
            public class Cmd
            {
                [Key(0)]
                public Item<int> Member { get; set; }
            }
        }
        """;

    [Fact]
    public async Task AnalyzerDoesNotThrowOnMetadataGenericStructWithNullKey()
    {
        ImmutableArray<MetadataReference> frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = frameworkRefs.Concat<MetadataReference>(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();

        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(LibSource) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var libStream = new MemoryStream();
        var emitResult = lib.Emit(libStream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        var appReferences = new List<MetadataReference>(references) { MetadataReference.CreateFromImage(libStream.ToArray()) };
        CSharpCompilation app = CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource) },
            appReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var exceptions = new List<Exception>();
        var cwaOptions = new CompilationWithAnalyzersOptions(
            new Microsoft.CodeAnalysis.Diagnostics.AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
            (ex, analyzer, diag) => { lock (exceptions) { exceptions.Add(ex); } },
            concurrentAnalysis: false,
            logAnalyzerExecutionTime: false);

        ImmutableArray<DiagnosticAnalyzer> analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new MsgPack00xMessagePackAnalyzer());
        var withAnalyzers = app.WithAnalyzers(analyzers, cwaOptions);
        ImmutableArray<Diagnostic> diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync(CancellationToken.None);

        Assert.True(exceptions.Count == 0, "Analyzer threw: " + string.Join(Environment.NewLine, exceptions.Select(e => e.ToString())));
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
    }
}
