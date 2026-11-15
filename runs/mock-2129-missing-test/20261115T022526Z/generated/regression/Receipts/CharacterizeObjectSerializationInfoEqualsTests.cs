// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class CharacterizeObjectSerializationInfoEqualsTests
{
    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Latest);

    private static async Task<CSharpCompilation> CreateCompilationAsync(SyntaxTree tree)
    {
        var frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var refs = frameworkRefs.Cast<MetadataReference>().Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack).Cast<MetadataReference>()).ToList();
        return CSharpCompilation.Create("TestAssembly", new[] { tree }, refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static GeneratorDriver CreateDriver()
    {
        return CSharpGeneratorDriver.Create(new ISourceGenerator[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() }, parseOptions: ParseOptions);
    }

    private static Dictionary<string, string> Sources(GeneratorDriverRunResult run)
    {
        return run.Results.SelectMany(r => r.GeneratedSources).ToDictionary(s => s.HintName, s => s.SourceText.ToString());
    }

    private static void AssertNoGeneratorFailure(GeneratorDriverRunResult run)
    {
        Assert.All(run.Results, r => Assert.Null(r.Exception));
        Assert.DoesNotContain(run.Diagnostics, d => d.Id == "CS8785");
    }

    private const string DataSource = """
        using MessagePack;

        namespace MyNs
        {
            [GeneratedMessagePackResolver]
            partial class MyResolver { }
        }

        [MessagePackObject]
        public class Holder
        {
            [Key(0)]
            public int AlphaValue { get; set; }

            [Key(1)]
            public string Name { get; set; }
        }

        [MessagePackObject]
        public struct Point
        {
            [Key(0)]
            public int X;

            [Key(1)]
            public int Y;
        }
        """;

    [Fact]
    public async Task RerunWithUnrelatedEdit_ProducesSameSourcesWithoutFailure()
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(DataSource, ParseOptions);
        CSharpCompilation compilation = await CreateCompilationAsync(tree);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGenerators(compilation);
        GeneratorDriverRunResult first = driver.GetRunResult();
        AssertNoGeneratorFailure(first);
        Dictionary<string, string> firstSources = Sources(first);
        Assert.Contains(firstSources.Values, s => s.Contains("HolderFormatter"));

        CSharpCompilation second = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("public class Unrelated { public int Z; }", ParseOptions));
        driver = driver.RunGenerators(second);
        GeneratorDriverRunResult secondRun = driver.GetRunResult();
        AssertNoGeneratorFailure(secondRun);
        Dictionary<string, string> secondSources = Sources(secondRun);

        Assert.Equal(firstSources.Keys.OrderBy(k => k), secondSources.Keys.OrderBy(k => k));
        foreach (var kv in firstSources)
        {
            Assert.Equal(kv.Value, secondSources[kv.Key]);
        }
    }

    [Fact]
    public async Task RerunWithChangedMember_RegeneratesFormatter()
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(DataSource, ParseOptions);
        CSharpCompilation compilation = await CreateCompilationAsync(tree);
        GeneratorDriver driver = CreateDriver();

        driver = driver.RunGenerators(compilation);
        GeneratorDriverRunResult first = driver.GetRunResult();
        AssertNoGeneratorFailure(first);
        Assert.Contains(Sources(first).Values, s => s.Contains("AlphaValue"));

        SyntaxTree changed = CSharpSyntaxTree.ParseText(DataSource.Replace("AlphaValue", "BetaValue"), ParseOptions);
        CSharpCompilation second = compilation.ReplaceSyntaxTree(tree, changed);
        driver = driver.RunGenerators(second);
        GeneratorDriverRunResult secondRun = driver.GetRunResult();
        AssertNoGeneratorFailure(secondRun);
        Dictionary<string, string> secondSources = Sources(secondRun);

        Assert.Contains(secondSources.Values, s => s.Contains("BetaValue"));
        Assert.DoesNotContain(secondSources.Values, s => s.Contains("AlphaValue"));
    }
}
