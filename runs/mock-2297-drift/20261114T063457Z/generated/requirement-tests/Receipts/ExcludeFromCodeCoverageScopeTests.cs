// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageScopeTests
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
            public class MyObject
            {
                [Key(0)]
                public int Value { get; set; }

                [Key(1)]
                public MyEnum E { get; set; }
            }

            [MessagePackObject(true)]
            public class MyMapObject
            {
                public string Name { get; set; }
            }

            public enum MyEnum
            {
                A,
                B,
            }

            [Union(0, typeof(UnionA))]
            public interface IMyUnion
            {
            }

            [MessagePackObject]
            public class UnionA : IMyUnion
            {
                [Key(0)]
                public int X { get; set; }
            }
        }
        """;

    [Fact]
    public async Task FormattersAreExcludedWhenTargetingNetFramework()
    {
        await AssertFormattersExcludedAsync("NETFRAMEWORK", "NET472");
    }

    [Fact]
    public async Task FormattersAreExcludedWhenTargetingNetStandard20()
    {
        await AssertFormattersExcludedAsync("NETSTANDARD", "NETSTANDARD2_0");
    }

    [Fact]
    public async Task AllGeneratedOnlyTypesAreExcludedFromCodeCoverage()
    {
        List<INamedTypeSymbol> generatedTypes = await GetGeneratedOnlyTypesAsync();
        Assert.NotEmpty(generatedTypes);
        foreach (INamedTypeSymbol type in generatedTypes)
        {
            Assert.True(IsExcludedFromCodeCoverage(type), $"{type.ToDisplayString()} is generated but not decorated with ExcludeFromCodeCoverage");
        }
    }

    private static async Task AssertFormattersExcludedAsync(params string[] preprocessorSymbols)
    {
        List<INamedTypeSymbol> generatedTypes = await GetGeneratedOnlyTypesAsync(preprocessorSymbols);
        List<INamedTypeSymbol> formatters = generatedTypes.Where(t => t.AllInterfaces.Any(i => i.Name == "IMessagePackFormatter")).ToList();
        Assert.NotEmpty(formatters);
        foreach (INamedTypeSymbol formatter in formatters)
        {
            Assert.True(IsExcludedFromCodeCoverage(formatter), $"{formatter.ToDisplayString()} is not decorated with ExcludeFromCodeCoverage");
        }
    }

    private static bool IsExcludedFromCodeCoverage(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? t = type; t is not null; t = t.ContainingType)
        {
            if (t.GetAttributes().Any(a => a.AttributeClass?.Name == "ExcludeFromCodeCoverageAttribute"))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<List<INamedTypeSymbol>> GetGeneratedOnlyTypesAsync(params string[] preprocessorSymbols)
    {
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: preprocessorSymbols);
        ImmutableArray<MetadataReference> references = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        references = references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(Source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        HashSet<SyntaxTree> generatedTrees = new HashSet<SyntaxTree>(output.SyntaxTrees.Except(compilation.SyntaxTrees));
        Assert.NotEmpty(generatedTrees);

        HashSet<INamedTypeSymbol> seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        List<INamedTypeSymbol> result = new List<INamedTypeSymbol>();
        foreach (SyntaxTree tree in generatedTrees)
        {
            SemanticModel model = output.GetSemanticModel(tree);
            foreach (BaseTypeDeclarationSyntax declaration in tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration) is INamedTypeSymbol symbol
                    && symbol.DeclaringSyntaxReferences.All(r => generatedTrees.Contains(r.SyntaxTree))
                    && seen.Add(symbol))
                {
                    result.Add(symbol);
                }
            }
        }

        return result;
    }
}
