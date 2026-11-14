// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class MetadataGenericStructDiagnosticFilterTests(ITestOutputHelper logger)
{
    [Fact]
    public async Task MetadataMemberMissingKey_IsNotReportedInConsumingCompilation()
    {
        MetadataReference lib = await BuildLibAsync();

        string appSource = """
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

        var test = new VerifyCS.Test
        {
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
        };
        test.TestState.Sources.Add(appSource);
        test.TestState.AdditionalReferences.Add(lib);
        logger.WriteLine("Running generator and analyzers against app referencing in-memory lib.");
        await test.RunAsync();
    }

    private static async Task<MetadataReference> BuildLibAsync()
    {
        string libSource = """
            using MessagePack;

            namespace Lib
            {
                [MessagePackObject]
                public struct Item<T>
                {
                    [Key(0)]
                    public string Key { get; set; }

                    public int Other { get; set; }
                }
            }
            """;

        var references = new List<MetadataReference>();
        references.AddRange(await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None));
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(libSource) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }
}
