// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class StringKeyMemberDroppedTests
{
    private readonly ITestOutputHelper logger;

    public StringKeyMemberDroppedTests(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    [Fact]
    public async Task StringKeyedFieldAndPropertyAppearInGeneratedFormatter()
    {
        string source = """
            using MessagePack;

            namespace TestNs
            {
                [MessagePackObject]
                public class MyObject
                {
                    [Key("f")]
                    public int FieldValue;

                    [Key("p")]
                    public int PropValue { get; set; }
                }

                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }
            }
            """;

        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        var compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGenerators(compilation);
        GeneratorDriverRunResult result = driver.GetRunResult();

        foreach (var d in result.Diagnostics)
        {
            this.logger.WriteLine(d.ToString());
        }

        string all = string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));
        this.logger.WriteLine(all);

        Assert.NotEmpty(result.GeneratedTrees);
        Assert.Contains("FieldValue", all);
        Assert.Contains("PropValue", all);
    }
}
