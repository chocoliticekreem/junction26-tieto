// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageEmittedTests
{
    private const string AttributeName = "System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute";

    private const string Source = """
        using MessagePack;

        namespace TestNs
        {
            [MessagePackObject]
            public class IntKeyed
            {
                [Key(0)]
                public int A { get; set; }

                [Key(1)]
                public MyEnum E { get; set; }
            }

            [MessagePackObject(true)]
            public class StringKeyed
            {
                public int B { get; set; }
            }

            public enum MyEnum { X, Y }

            [Union(0, typeof(UnionA))]
            public interface IUnion { }

            [MessagePackObject]
            public class UnionA : IUnion
            {
                [Key(0)]
                public int C { get; set; }
            }

            [GeneratedMessagePackResolver]
            public partial class MyResolver { }
        }
        """;

    [Fact]
    public async Task AllGeneratedTypesAreExcludedFromCodeCoverage()
    {
        List<INamedTypeSymbol> types = await GetGeneratedTypesAsync();
        Assert.NotEmpty(types);
        List<string> missing = types.Where(t => !HasAttribute(t)).Select(t => t.ToDisplayString()).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public async Task GeneratedFormattersAndResolverAreExcludedFromCodeCoverage()
    {
        List<INamedTypeSymbol> types = await GetGeneratedTypesAsync();
        List<INamedTypeSymbol> formatters = types.Where(t => t.Name.EndsWith("Formatter", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(formatters);
        Assert.All(formatters, f => Assert.True(HasAttribute(f), f.ToDisplayString()));

        INamedTypeSymbol? resolver = types.FirstOrDefault(t => t.Name == "MyResolver");
        Assert.NotNull(resolver);
        Assert.True(HasAttribute(resolver!), "MyResolver");
    }

    private static bool HasAttribute(INamedTypeSymbol type)
        => type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == AttributeName);

    private static bool HasOwnCode(INamedTypeSymbol type)
        => type.GetMembers().Any(m => !m.IsImplicitlyDeclared && m is not INamedTypeSymbol);

    private static async Task<List<INamedTypeSymbol>> GetGeneratedTypesAsync()
    {
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        ImmutableArray<MetadataReference> frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = Enumerable.Concat<MetadataReference>(frameworkRefs, ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsCoverageTest",
            new[] { CSharpSyntaxTree.ParseText(Source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        Assert.DoesNotContain(generatorDiagnostics, d => d.Severity == DiagnosticSeverity.Error);

        List<SyntaxTree> generatedTrees = output.SyntaxTrees.Where(t => !compilation.SyntaxTrees.Contains(t)).ToList();
        Assert.NotEmpty(generatedTrees);

        HashSet<INamedTypeSymbol> seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        List<INamedTypeSymbol> result = new List<INamedTypeSymbol>();
        foreach (SyntaxTree tree in generatedTrees)
        {
            SemanticModel model = output.GetSemanticModel(tree);
            foreach (TypeDeclarationSyntax decl in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(decl) is INamedTypeSymbol symbol && HasOwnCode(symbol) && seen.Add(symbol))
                {
                    result.Add(symbol);
                }
            }
        }

        return result;
    }
}
