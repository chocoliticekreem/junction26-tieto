// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class StringKeyedMembersStillGeneratedTests
{
    private static async Task<string> GenerateAsync(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = frameworkRefs.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        var compilation = CSharpCompilation.Create(
            "ReceiptsGenAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGenerators(compilation);
        var result = driver.GetRunResult();
        Assert.All(result.Results, r => Assert.Null(r.Exception));
        return string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));
    }

    [Fact]
    public async Task StringKeyedFields_AppearInGeneratedFormatter()
    {
        string source = """
            using MessagePack;

            namespace TestNs
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver { }

                [MessagePackObject]
                public class StringKeyedFieldsObject
                {
                    [Key("k1")]
                    public int AlphaFieldValue;

                    [Key("k2")]
                    public int BetaFieldValue;
                }
            }
            """;
        string generated = await GenerateAsync(source);
        Assert.Contains("StringKeyedFieldsObject", generated);
        Assert.Contains("AlphaFieldValue", generated);
        Assert.Contains("BetaFieldValue", generated);
    }

    [Fact]
    public async Task StringKeyedProperties_AppearInGeneratedFormatter()
    {
        string source = """
            using MessagePack;

            namespace TestNs
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver { }

                [MessagePackObject]
                public class StringKeyedPropsObject
                {
                    [Key("k1")]
                    public int GammaPropValue { get; set; }

                    [Key("k2")]
                    public int DeltaPropValue { get; set; }
                }
            }
            """;
        string generated = await GenerateAsync(source);
        Assert.Contains("StringKeyedPropsObject", generated);
        Assert.Contains("GammaPropValue", generated);
        Assert.Contains("DeltaPropValue", generated);
    }
}
