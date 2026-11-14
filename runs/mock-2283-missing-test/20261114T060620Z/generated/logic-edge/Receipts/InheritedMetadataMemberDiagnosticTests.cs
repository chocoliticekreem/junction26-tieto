// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class InheritedMetadataMemberDiagnosticTests
{
    private readonly ITestOutputHelper logger;

    public InheritedMetadataMemberDiagnosticTests(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    [Fact]
    public async Task MissingKeyOnInheritedMetadataMember_IsStillReported()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> refs = framework.Concat<MetadataReference>(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();

        string libSource = """
            namespace Lib
            {
                public class BaseType
                {
                    public int Unkeyed { get; set; }
                }
            }
            """;

        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(libSource) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var ms = new MemoryStream();
        var emitResult = lib.Emit(ms);
        emitResult.Success.Should().BeTrue(string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference libRef = MetadataReference.CreateFromImage(ms.ToArray());

        string appSource = """
            using MessagePack;

            namespace App
            {
                [MessagePackObject]
                public class Derived : Lib.BaseType
                {
                    [Key(0)]
                    public int X { get; set; }
                }
            }
            """;

        CSharpCompilation app = CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(appSource) },
            refs.Concat(new[] { libRef }),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        ImmutableArray<DiagnosticAnalyzer> analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new MessagePack.SourceGenerator.Analyzers.MsgPack00xMessagePackAnalyzer());
        ImmutableArray<Diagnostic> diagnostics = await app.WithAnalyzers(analyzers).GetAllDiagnosticsAsync();
        foreach (Diagnostic d in diagnostics)
        {
            this.logger.WriteLine(d.ToString());
        }

        diagnostics.Should().NotContain(d => d.Id == "AD0001");
        diagnostics.Should().Contain(d => d.Id == "MsgPack004", "the source type Derived exposes an unkeyed public member inherited from a referenced assembly, and no other compilation validates it");
    }
}
