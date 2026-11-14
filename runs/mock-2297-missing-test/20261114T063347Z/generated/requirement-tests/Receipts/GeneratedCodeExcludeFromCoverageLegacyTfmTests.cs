// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedCodeExcludeFromCoverageLegacyTfmTests
{
    private const string Source = """
        using MessagePack;

        namespace TestNs
        {
            [GeneratedMessagePackResolver]
            partial class MyResolver { }

            [MessagePackObject]
            public class IntKeyed
            {
                [Key(0)]
                public int A { get; set; }
            }

            [MessagePackObject(true)]
            public class StringKeyed
            {
                public int B { get; set; }
            }
        }
        """;

    [Theory]
    [InlineData("NETSTANDARD2_0")]
    [InlineData("NETFRAMEWORK")]
    public async Task GeneratedTypesAreExcludedOnLegacyTargets(string symbol)
    {
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3).WithPreprocessorSymbols(symbol);

        ImmutableArray<MetadataReference> frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsLegacyAssembly",
            new[] { CSharpSyntaxTree.ParseText(Source, parseOptions) },
            frameworkRefs.Concat<MetadataReference>(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGenerators(compilation);
        GeneratorDriverRunResult result = driver.GetRunResult();

        List<ClassDeclarationSyntax> classes = result.GeneratedTrees
            .SelectMany(t => t.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Where(c => c.BaseList is not null && c.BaseList.Types.Any(b => b.ToString().Contains("IMessagePackFormatter") || b.ToString().Contains("IFormatterResolver")))
            .ToList();

        Assert.NotEmpty(classes);
        foreach (ClassDeclarationSyntax c in classes)
        {
            bool hasAttribute = c.AttributeLists.SelectMany(l => l.Attributes).Any(a => a.Name.ToString().Contains("ExcludeFromCodeCoverage"));
            Assert.True(hasAttribute, $"With {symbol} defined, generated type {c.Identifier.ValueText} is not decorated with ExcludeFromCodeCoverage.");
        }
    }
}
