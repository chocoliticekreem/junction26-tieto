// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class Req2297CoverageAttributeTests
{
    private const string Source = """
        using MessagePack;

        namespace TestNs
        {
            [MessagePackObject]
            public class IntKeyed
            {
                [Key(0)]
                public int A { get; set; }
            }

            [MessagePackObject]
            public class StringKeyed
            {
                [Key("b")]
                public int B { get; set; }
            }

            public enum Color { Red, Green }

            [MessagePackObject]
            public class HasEnum
            {
                [Key(0)]
                public Color C { get; set; }
            }

            [Union(0, typeof(UnionA))]
            public interface IUnion { }

            [MessagePackObject]
            public class UnionA : IUnion
            {
                [Key(0)]
                public int X { get; set; }
            }

            [GeneratedMessagePackResolver]
            partial class MyResolver { }
        }
        """;

    private static async Task<(Compilation Output, ImmutableArray<SyntaxTree> Generated)> RunGeneratorAsync(params string[] symbols)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3, preprocessorSymbols: symbols);
        var frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = frameworkRefs.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        Compilation compilation = CSharpCompilation.Create(
            "Req2297TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(Source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        return (output, driver.GetRunResult().GeneratedTrees);
    }

    private static bool HasAttr(INamedTypeSymbol? type)
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

    private static void AssertAllGeneratedTypesExcluded(Compilation output, ImmutableArray<SyntaxTree> generated)
    {
        Assert.NotEmpty(generated);
        int formatterCount = 0;
        var missing = new List<string>();
        foreach (SyntaxTree tree in generated)
        {
            SemanticModel model = output.GetSemanticModel(tree);
            foreach (ClassDeclarationSyntax cls in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                var symbol = model.GetDeclaredSymbol(cls) as INamedTypeSymbol;
                if (symbol is null)
                {
                    continue;
                }

                if (symbol.AllInterfaces.Any(i => i.Name == "IMessagePackFormatter" && i.IsGenericType))
                {
                    formatterCount++;
                }

                if (!HasAttr(symbol))
                {
                    missing.Add(symbol.ToDisplayString());
                }
            }
        }

        Assert.True(formatterCount > 0, "No generated formatter types were found.");
        Assert.True(missing.Count == 0, "Generated types without ExcludeFromCodeCoverage: " + string.Join(", ", missing));
    }

    [Fact]
    public async Task AllGeneratedTypesAreExcludedFromCodeCoverage()
    {
        var (output, generated) = await RunGeneratorAsync();
        AssertAllGeneratedTypesExcluded(output, generated);
    }

    [Fact]
    public async Task GeneratedResolverIsExcludedFromCodeCoverage()
    {
        var (output, _) = await RunGeneratorAsync();
        INamedTypeSymbol? resolver = output.GetTypeByMetadataName("TestNs.MyResolver");
        Assert.NotNull(resolver);
        Assert.True(HasAttr(resolver), "Generated resolver lacks ExcludeFromCodeCoverage.");
    }
}
