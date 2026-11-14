// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageReceiptTests
{
    private const string Source = """
        using MessagePack;

        namespace Test
        {
            [MessagePackObject]
            public class MyObject
            {
                [Key(0)]
                public int Value { get; set; }

                [Key(1)]
                public MyEnum E { get; set; }
            }

            public enum MyEnum { A, B }

            [GeneratedMessagePackResolver]
            partial class MyResolver
            {
            }
        }
        """;

    private static async Task<GeneratorDriverRunResult> RunGeneratorAsync(CSharpParseOptions parseOptions)
    {
        ImmutableArray<MetadataReference> refs = (await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None))
            .AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsAsm",
            new[] { CSharpSyntaxTree.ParseText(Source, parseOptions) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGenerators(compilation);
        return driver.GetRunResult();
    }

    [Fact]
    public async Task EveryGeneratedSourceMentionsExcludeFromCodeCoverage()
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3);
        GeneratorDriverRunResult result = await RunGeneratorAsync(parseOptions);
        var sources = result.Results.SelectMany(r => r.GeneratedSources).ToList();
        Assert.NotEmpty(sources);
        var missing = sources
            .Where(s => !s.SourceText.ToString().Contains("ExcludeFromCodeCoverage"))
            .Select(s => s.HintName)
            .ToList();
        Assert.True(missing.Count == 0, "Generated sources without ExcludeFromCodeCoverage: " + string.Join(", ", missing));
    }

    [Theory]
    [InlineData("NETFRAMEWORK")]
    [InlineData("NETSTANDARD2_0")]
    public async Task FormattersKeepAttributeWhenNetFrameworkOrNetStandard20IsDefined(string symbol)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3, preprocessorSymbols: new[] { symbol });
        GeneratorDriverRunResult result = await RunGeneratorAsync(parseOptions);
        var formatterClasses = result.GeneratedTrees
            .SelectMany(t => t.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Where(c => c.BaseList is not null && c.BaseList.ToString().Contains("IMessagePackFormatter"))
            .ToList();
        Assert.NotEmpty(formatterClasses);
        var missing = formatterClasses
            .Where(c => !c.AttributeLists.Any(al => al.ToString().Contains("ExcludeFromCodeCoverage")))
            .Select(c => c.Identifier.Text)
            .ToList();
        Assert.True(missing.Count == 0, $"With {symbol} defined these formatters lack ExcludeFromCodeCoverage: " + string.Join(", ", missing));
    }
}
