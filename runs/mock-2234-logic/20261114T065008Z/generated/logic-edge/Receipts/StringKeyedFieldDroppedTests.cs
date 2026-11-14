// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class StringKeyedFieldDroppedTests
{
    private readonly ITestOutputHelper logger;

    public StringKeyedFieldDroppedTests(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    [Fact]
    public async Task StringKeyedFieldsAppearInGeneratedFormatter()
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
        [Key("a")]
        public int MyFieldA;

        [Key("b")]
        public int MyFieldB;
    }
}
""";

        var framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = framework.Concat<MetadataReference>(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        var compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp7_3)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator());
        driver = driver.RunGenerators(compilation);
        GeneratorDriverRunResult result = driver.GetRunResult();

        foreach (var r in result.Results)
        {
            Assert.Null(r.Exception);
        }

        string allGenerated = string.Join("\n", result.GeneratedTrees.Select(t => t.GetText().ToString()));
        this.logger.WriteLine(allGenerated);

        Assert.NotEmpty(result.GeneratedTrees);
        Assert.Contains("MyFieldA", allGenerated);
        Assert.Contains("MyFieldB", allGenerated);
    }
}
