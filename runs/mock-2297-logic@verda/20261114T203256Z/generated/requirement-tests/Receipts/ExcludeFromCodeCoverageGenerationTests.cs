// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageGenerationTests
{
    private const string AttributeName = "global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage";

    [Fact]
    public async Task ObjectFormatter_HasActiveExcludeFromCodeCoverageAttribute()
    {
        GeneratorDriverRunResult result = await RunGeneratorAsync();
        string formatter = result.GeneratedTrees
            .Select(tree => tree.ToString())
            .Single(text => text.Contains("IMessagePackFormatter<global::MyObject>", StringComparison.Ordinal));

        Assert.True(CountActiveAttributes(formatter) > 0, formatter);
    }

    [Fact]
    public async Task ResolverAndNestedHelpers_HaveActiveExcludeFromCodeCoverageAttributes()
    {
        GeneratorDriverRunResult result = await RunGeneratorAsync();
        string resolver = result.GeneratedTrees
            .Select(tree => tree.ToString())
            .Single(text => text.Contains("partial class TestResolver", StringComparison.Ordinal));

        Assert.Equal(3, CountActiveAttributes(resolver));
    }

    private static async Task<GeneratorDriverRunResult> RunGeneratorAsync()
    {
        const string source = """
            using MessagePack;

            [MessagePackObject]
            public class MyObject
            {
                [Key(0)]
                public int Value { get; set; }
            }

            [GeneratedMessagePackResolver]
            partial class TestResolver
            {
            }
            """;

        MetadataReference[] references = (await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None))
            .AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack))
            .ToArray();
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Receipts.GeneratedTests.ObjectAndResolver",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    private static int CountActiveAttributes(string generatedSource)
    {
        return CSharpSyntaxTree.ParseText(generatedSource, new CSharpParseOptions(LanguageVersion.Latest))
            .GetRoot()
            .DescendantNodes()
            .OfType<AttributeSyntax>()
            .Count(attribute => attribute.Name.ToString() == AttributeName);
    }
}
