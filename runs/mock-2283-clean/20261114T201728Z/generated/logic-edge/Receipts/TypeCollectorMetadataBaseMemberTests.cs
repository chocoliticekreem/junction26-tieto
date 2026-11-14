// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class TypeCollectorMetadataBaseMemberTests(ITestOutputHelper logger)
{
    [Fact]
    public async Task InheritedUnkeyedPublicMemberFromMetadataBase_StillReportsMsgPack004()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> refs = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();

        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest);

        string libSource = """
            namespace Lib
            {
                public class BaseData
                {
                    public int Unkeyed { get; set; }
                }
            }
            """;

        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(libSource, parseOptions) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using MemoryStream ms = new MemoryStream();
        var emitResult = lib.Emit(ms);
        emitResult.Success.Should().BeTrue(string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference libRef = MetadataReference.CreateFromImage(ms.ToArray());

        string appSource = """
            using MessagePack;

            namespace App
            {
                [MessagePackObject]
                public class Derived : Lib.BaseData
                {
                    [Key(0)]
                    public int Keyed { get; set; }
                }
            }
            """;

        CSharpCompilation app = CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(appSource, parseOptions) },
            refs.Append(libRef),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        DiagnosticAnalyzer analyzer = new MessagePack.SourceGenerator.Analyzers.MsgPack00xMessagePackAnalyzer();
        CompilationWithAnalyzers withAnalyzers = app.WithAnalyzers(ImmutableArray.Create(analyzer));
        ImmutableArray<Diagnostic> diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();

        foreach (Diagnostic d in diagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        diagnostics.Should().NotContain(d => d.Id == "AD0001");
        diagnostics.Should().Contain(d => d.Id == "MsgPack004", "the public inherited member 'Unkeyed' of a source-declared [MessagePackObject] type has no [Key] and no other assembly validates it");
    }
}
