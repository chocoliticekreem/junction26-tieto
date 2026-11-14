// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageDownlevelTfmTests
{
    private const string Source = """
        using MessagePack;

        namespace MyApp
        {
            [MessagePackObject]
            public class IntKeyed
            {
                [Key(0)] public int A { get; set; }
            }

            [MessagePackObject(true)]
            public class StringKeyed
            {
                public int B { get; set; }
            }

            [GeneratedMessagePackResolver]
            partial class MyResolver { }
        }
        """;

    [Theory]
    [InlineData("NETSTANDARD2_0")]
    [InlineData("NETFRAMEWORK")]
    public async Task GeneratedTypesAreExcludedWhenTargetingDownlevelFrameworks(string symbol)
    {
        CSharpParseOptions parseOptions = CSharpParseOptions.Default.WithPreprocessorSymbols(symbol);
        (CSharpCompilation compilation, ImmutableArray<SyntaxTree> generated) = await GenerateAsync(Source, parseOptions);

        List<INamedTypeSymbol?> types = generated.SelectMany(tree =>
        {
            SemanticModel model = compilation.GetSemanticModel(tree);
            return tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Select(d => model.GetDeclaredSymbol(d));
        }).ToList();

        Assert.NotEmpty(types);
        Assert.All(types, t => Assert.True(IsExcluded(t), $"With {symbol} defined, generated type {t?.ToDisplayString()} is not decorated with ExcludeFromCodeCoverage."));
    }

    private static bool IsExcluded(INamedTypeSymbol? type)
    {
        for (INamedTypeSymbol? t = type; t is not null; t = t.ContainingType)
        {
            if (t.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute"))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<(CSharpCompilation Compilation, ImmutableArray<SyntaxTree> Generated)> GenerateAsync(string source, CSharpParseOptions parseOptions)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = framework.Concat<MetadataReference>(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsDownlevelAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator());
        driver = driver.RunGenerators(compilation);
        GeneratorDriverRunResult result = driver.GetRunResult();
        Assert.All(result.Results, r => Assert.Null(r.Exception));

        ImmutableArray<SyntaxTree> generated = result.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => CSharpSyntaxTree.ParseText(s.SourceText, parseOptions, s.HintName))
            .ToImmutableArray();

        return (compilation.AddSyntaxTrees(generated), generated);
    }
}
