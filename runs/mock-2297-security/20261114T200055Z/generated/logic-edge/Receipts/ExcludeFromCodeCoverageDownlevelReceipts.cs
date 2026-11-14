// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageDownlevelReceipts
{
    private const string Source = """
        using MessagePack;

        namespace Test
        {
            [GeneratedMessagePackResolver]
            partial class MyResolver
            {
            }

            [MessagePackObject]
            public class MyObject
            {
                [Key(0)]
                public int Value { get; set; }
            }
        }
        """;

    [Theory]
    [InlineData("NETSTANDARD2_0")]
    [InlineData("NETFRAMEWORK")]
    public async Task GeneratedResolverIsExcludedFromCoverage_OnDownlevelTargets(string symbol)
    {
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: new[] { symbol });
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsAssembly",
            new[] { CSharpSyntaxTree.ParseText(Source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);

        GeneratorDriverRunResult runResult = driver.GetRunResult();
        Assert.NotEmpty(runResult.GeneratedTrees);

        INamedTypeSymbol? resolver = output.GetTypeByMetadataName("Test.MyResolver");
        Assert.NotNull(resolver);
        List<string?> attributeNames = resolver!.GetAttributes().Select(a => a.AttributeClass?.ToDisplayString()).ToList();
        Assert.Contains("System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute", attributeNames);
    }
}
