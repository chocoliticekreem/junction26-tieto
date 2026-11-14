// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class MetadataGenericStructNullKeyTests
{
    private readonly ITestOutputHelper logger;

    public MetadataGenericStructNullKeyTests(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    [Fact]
    public async Task AnalyzerDoesNotThrowOnMetadataPropertyWithNullKey()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> refs = new List<MetadataReference>(framework);
        refs.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        string libSource = """
            using MessagePack;
            namespace Lib
            {
                [MessagePackObject]
                public struct Item<T>
                {
                    [Key(null)]
                    public string Name { get; set; }
                }
            }
            """;
        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(libSource) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream ms = new MemoryStream();
        var emit = lib.Emit(ms);
        foreach (var d in emit.Diagnostics)
        {
            this.logger.WriteLine(d.ToString());
        }

        Assert.True(emit.Success);
        MetadataReference libRef = MetadataReference.CreateFromImage(ms.ToArray());

        string appSource = """
            using MessagePack;
            using Lib;
            namespace App
            {
                [MessagePackObject]
                public partial class Cmd
                {
                    [Key(0)]
                    public Item<int> Member { get; set; }
                }
            }
            """;
        List<MetadataReference> appRefs = new List<MetadataReference>(refs) { libRef };
        CSharpCompilation app = CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(appSource) },
            appRefs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        ImmutableArray<DiagnosticAnalyzer> analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new MessagePack.SourceGenerator.Analyzers.MsgPack00xMessagePackAnalyzer());
        ImmutableArray<Diagnostic> diagnostics = await app.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        foreach (var d in diagnostics)
        {
            this.logger.WriteLine(d.ToString());
        }

        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
    }
}
