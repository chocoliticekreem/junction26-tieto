// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using FluentAssertions;
using MessagePack.SourceGenerator.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class MetadataGenericStructDiagnosticsTests(ITestOutputHelper logger)
{
    private const string LibSource = """
        using MessagePack;
        namespace Lib
        {
            [MessagePackObject]
            public struct Item<T>
            {
                [Key(0)]
                public string Key { get; set; }

                public int Unkeyed { get; set; }
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
    public async Task MetadataGenericStruct_UnkeyedMember_NotReportedInConsumer()
    {
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        MetadataReference[] baseRefs = Enumerable.Concat<MetadataReference>(framework, ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToArray();

        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(LibSource, parseOptions) },
            baseRefs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new MemoryStream();
        var emitResult = lib.Emit(stream);
        foreach (Diagnostic d in emitResult.Diagnostics)
        {
            logger.WriteLine("lib: " + d.ToString());
        }

        emitResult.Success.Should().BeTrue();
        MetadataReference libRef = MetadataReference.CreateFromImage(stream.ToArray());

        CSharpCompilation app = CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource, parseOptions) },
            baseRefs.Concat(new[] { libRef }),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        ImmutableArray<DiagnosticAnalyzer> analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new MsgPack00xMessagePackAnalyzer());
        ImmutableArray<Diagnostic> diagnostics = await app.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        foreach (Diagnostic d in diagnostics)
        {
            logger.WriteLine("app: " + d.ToString());
        }

        diagnostics.Where(d => d.Id == "AD0001").Should().BeEmpty();
        diagnostics.Where(d => d.Id == "MsgPack004").Should().BeEmpty("members of types defined in a referenced assembly are validated by that assembly, not the consumer");
    }
}
