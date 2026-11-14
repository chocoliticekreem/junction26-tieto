// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class DuplicateKeySilentDropTests(ITestOutputHelper logger)
{
    [Fact]
    public async Task DuplicateIntKey_DoesNotSilentlyDropMember()
    {
        string source = """
            using MessagePack;

            namespace TestNs
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver { }

                [MessagePackObject]
                public class MyObject
                {
                    [Key(0)]
                    public int AlphaValue { get; set; }

                    [Key(0)]
                    public int BetaValue { get; set; }
                }
            }
            """;

        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3);
        var frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = new List<MetadataReference>();
        references.AddRange(frameworkRefs);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsDuplicateKey",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        ISourceGenerator generator = new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { generator }, parseOptions: parseOptions);
        driver = driver.RunGenerators(compilation);
        GeneratorDriverRunResult result = driver.GetRunResult();

        foreach (Diagnostic d in result.Diagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        string combined = string.Join("\n", result.GeneratedTrees.Select(t => t.GetText().ToString()));
        logger.WriteLine(combined);

        if (combined.Contains("AlphaValue"))
        {
            Assert.True(combined.Contains("BetaValue"), "Generated formatter serializes AlphaValue but silently drops BetaValue, which shares the same Key(0).");
        }
    }
}
