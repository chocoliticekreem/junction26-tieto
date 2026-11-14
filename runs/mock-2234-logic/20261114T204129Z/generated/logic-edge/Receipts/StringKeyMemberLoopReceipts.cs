// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class StringKeyMemberLoopReceipts
{
    private readonly ITestOutputHelper logger;

    public StringKeyMemberLoopReceipts(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    [Fact]
    public async Task StringKeyedFieldAndPropertyAppearInGeneratedFormatter()
    {
        string source = """
            using MessagePack;

            namespace ReceiptsNs
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }

                [MessagePackObject]
                public class FieldHolder
                {
                    [Key("k1")]
                    public int UniqueFieldNameAlpha;

                    [Key("k2")]
                    public string UniqueFieldNameBeta;
                }

                [MessagePackObject]
                public class PropertyHolder
                {
                    [Key("p1")]
                    public int UniquePropNameGamma { get; set; }

                    [Key("p2")]
                    public string UniquePropNameDelta { get; set; }
                }
            }
            """;

        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3);
        var frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = frameworkRefs.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        GeneratorDriverRunResult result = driver.GetRunResult();

        foreach (var r in result.Results)
        {
            Assert.Null(r.Exception);
        }

        string generated = string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));
        this.logger.WriteLine(generated);

        Assert.Contains("UniqueFieldNameAlpha", generated);
        Assert.Contains("UniqueFieldNameBeta", generated);
        Assert.Contains("UniquePropNameGamma", generated);
        Assert.Contains("UniquePropNameDelta", generated);
    }
}
