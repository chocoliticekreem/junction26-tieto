// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageDefaultTests
{
    private const string AttributeName = "System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute";

    private const string Source = """
        using MessagePack;

        namespace Receipts.Sample
        {
            [GeneratedMessagePackResolver]
            partial class MyResolver
            {
            }

            public enum Color
            {
                Red,
                Green,
            }

            [MessagePackObject]
            public class IntKeyed
            {
                [Key(0)]
                public int A { get; set; }

                [Key(1)]
                public Color C { get; set; }
            }

            [MessagePackObject(true)]
            public class StringKeyed
            {
                public string B { get; set; }
            }

            [Union(0, typeof(UnionA))]
            public interface IUnion
            {
            }

            [MessagePackObject]
            public class UnionA : IUnion
            {
                [Key(0)]
                public int X { get; set; }
            }
        }
        """;

    [Fact]
    public async Task AllGeneratedTypesWithCodeAreExcludedFromCodeCoverageByDefault()
    {
        List<INamedTypeSymbol> types = await GetGeneratedCodeTypesAsync(new CSharpParseOptions(LanguageVersion.CSharp7_3));
        Assert.Contains(types, t => t.Name.EndsWith("Formatter", StringComparison.Ordinal));
        List<string> missing = types.Where(t => !HasExcludeAttribute(t)).Select(t => t.ToDisplayString()).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public async Task GeneratedResolverIsExcludedFromCodeCoverageByDefault()
    {
        List<INamedTypeSymbol> types = await GetGeneratedCodeTypesAsync(new CSharpParseOptions(LanguageVersion.CSharp7_3));
        INamedTypeSymbol? resolver = types.FirstOrDefault(t => t.Name == "MyResolver");
        Assert.NotNull(resolver);
        Assert.True(HasExcludeAttribute(resolver!), "The generated resolver should carry [ExcludeFromCodeCoverage].");
    }

    private static bool HasExcludeAttribute(INamedTypeSymbol type)
    {
        return type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == AttributeName);
    }

    private static async Task<List<INamedTypeSymbol>> GetGeneratedCodeTypesAsync(CSharpParseOptions parseOptions)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = new List<MetadataReference>(framework);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        SyntaxTree sourceTree = CSharpSyntaxTree.ParseText(Source, parseOptions);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsSample",
            new[] { sourceTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);

        Assert.All(driver.GetRunResult().Results, r => Assert.Null(r.Exception));
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        List<SyntaxTree> generatedTrees = output.SyntaxTrees.Where(t => t != sourceTree).ToList();
        Assert.NotEmpty(generatedTrees);

        List<INamedTypeSymbol> result = new List<INamedTypeSymbol>();
        foreach (SyntaxTree tree in generatedTrees)
        {
            SemanticModel model = output.GetSemanticModel(tree);
            foreach (BaseTypeDeclarationSyntax declaration in tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration) is INamedTypeSymbol symbol
                    && symbol.GetMembers().Any(m => m is not INamedTypeSymbol && !m.IsImplicitlyDeclared && m.DeclaringSyntaxReferences.Any(r => r.SyntaxTree == tree))
                    && !result.Contains(symbol, SymbolEqualityComparer.Default))
                {
                    result.Add(symbol);
                }
            }
        }

        return result;
    }
}
