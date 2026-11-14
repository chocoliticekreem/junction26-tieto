// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class Req2297CoverageGapTests
{
    private const string Source = """
        using MessagePack;

        namespace TestNs
        {
            [GeneratedMessagePackResolver]
            partial class MyResolver
            {
            }

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

            public enum MyEnum
            {
                X,
                Y,
            }

            [MessagePackObject]
            public class HasEnum
            {
                [Key(0)]
                public MyEnum E { get; set; }
            }

            [Union(0, typeof(UnionA))]
            public interface IMyUnion
            {
            }

            [MessagePackObject]
            public class UnionA : IMyUnion
            {
                [Key(0)]
                public int C { get; set; }
            }
        }
        """;

    [Fact]
    public async Task ResolverIsExcludedFromCodeCoverage()
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var (output, _) = await RunAsync(parseOptions);
        INamedTypeSymbol? resolver = output.GetTypeByMetadataName("TestNs.MyResolver");
        Assert.NotNull(resolver);
        Assert.True(IsExcluded(resolver), "The generated resolver should be decorated with ExcludeFromCodeCoverage.");
    }

    [Theory]
    [InlineData("NETSTANDARD2_0")]
    [InlineData("NETFRAMEWORK")]
    public async Task FormattersAreExcludedFromCodeCoverageForLegacyTargets(string symbol)
    {
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest).WithPreprocessorSymbols(symbol);
        var (output, generated) = await RunAsync(parseOptions);
        List<INamedTypeSymbol> formatters = GeneratedFormatters(output, generated);
        Assert.True(formatters.Count >= 4, $"Expected generated formatters, found {formatters.Count}");
        List<string> missing = formatters.Where(f => !IsExcluded(f)).Select(f => f.ToDisplayString()).ToList();
        Assert.Empty(missing);
    }

    private static async Task<(Compilation Output, List<SyntaxTree> Generated)> RunAsync(CSharpParseOptions parseOptions)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        SyntaxTree input = CSharpSyntaxTree.ParseText(Source, parseOptions);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsCoverageGap",
            new[] { input },
            Enumerable.Concat<MetadataReference>(framework, ReferencesHelper.GetReferences(ReferencesSet.MessagePack)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
        List<SyntaxTree> generated = output.SyntaxTrees.Where(t => t != input).ToList();
        return (output, generated);
    }

    private static List<INamedTypeSymbol> GeneratedFormatters(Compilation output, List<SyntaxTree> generated)
    {
        var result = new List<INamedTypeSymbol>();
        foreach (SyntaxTree tree in generated)
        {
            SemanticModel model = output.GetSemanticModel(tree);
            foreach (ClassDeclarationSyntax cls in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(cls) is INamedTypeSymbol symbol &&
                    symbol.AllInterfaces.Any(i => i.Name == "IMessagePackFormatter" && i.ContainingNamespace.ToDisplayString() == "MessagePack.Formatters"))
                {
                    result.Add(symbol);
                }
            }
        }

        return result;
    }

    private static bool IsExcluded(INamedTypeSymbol? type)
    {
        for (; type is not null; type = type.ContainingType)
        {
            if (type.GetAttributes().Any(a => a.AttributeClass?.Name == "ExcludeFromCodeCoverageAttribute"))
            {
                return true;
            }
        }

        return false;
    }
}
