// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack.SourceGenerator.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class InheritedMetadataMemberNeedsKeyTests
{
    private readonly ITestOutputHelper logger;

    public InheritedMetadataMemberNeedsKeyTests(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    [Fact]
    public async Task MemberNeedsKey_IsReported_ForPublicFieldInheritedFromReferencedAssembly()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest);

        string libSource = """
            namespace Lib
            {
                public class BaseType
                {
                    public int Unkeyed;
                }
            }
            """;

        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(libSource, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using MemoryStream stream = new MemoryStream();
        var emitResult = lib.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference libReference = MetadataReference.CreateFromImage(stream.ToArray());

        string appSource = """
            using MessagePack;

            namespace App
            {
                [MessagePackObject]
                public class Derived : Lib.BaseType
                {
                    [Key(0)]
                    public int Keyed;
                }
            }
            """;

        CSharpCompilation app = CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(appSource, parseOptions) },
            references.Append(libReference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CompilationWithAnalyzers withAnalyzers = app.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new MsgPack00xMessagePackAnalyzer()));
        ImmutableArray<Diagnostic> diagnostics = await withAnalyzers.GetAllDiagnosticsAsync();
        foreach (Diagnostic d in diagnostics)
        {
            this.logger.WriteLine(d.ToString());
        }

        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Contains(diagnostics, d => d.Id == "MsgPack004");
    }
}
