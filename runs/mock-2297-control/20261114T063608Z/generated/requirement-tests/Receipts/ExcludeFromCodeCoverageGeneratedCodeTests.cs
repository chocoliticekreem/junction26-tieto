// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageGeneratedCodeTests
{
    private const string AttributeName = "System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute";

    private const string Source = """
        using MessagePack;

        namespace MyNs
        {
            [GeneratedMessagePackResolver]
            partial class MyResolver { }

            [MessagePackObject]
            public class IntKeyed { [Key(0)] public int A { get; set; } }

            [MessagePackObject(true)]
            public class StringKeyed { public int B { get; set; } }

            public enum MyEnum { X, Y }

            [MessagePackObject]
            public class HasEnum { [Key(0)] public MyEnum E { get; set; } }

            [Union(0, typeof(UnionImpl))]
            public interface IUnionBase { }

            [MessagePackObject]
            public class UnionImpl : IUnionBase { [Key(0)] public int C { get; set; } }
        }
        """;

    [Fact]
    public async Task GeneratedResolverIsExcludedFromCodeCoverage()
    {
        (Compilation output, _) = await RunGeneratorAsync();
        INamedTypeSymbol? resolver = output.GetTypeByMetadataName("MyNs.MyResolver");
        Assert.NotNull(resolver);
        Assert.True(HasAttribute(resolver!), "Generated resolver MyNs.MyResolver is not decorated with ExcludeFromCodeCoverage.");
    }

    [Fact]
    public async Task AllGeneratedTypesAreExcludedFromCodeCoverage()
    {
        (Compilation output, ImmutableArray<SyntaxTree> generated) = await RunGeneratorAsync();
        var uncovered = new List<string>();
        int checkedTypes = 0;
        foreach (SyntaxTree tree in generated)
        {
            SemanticModel model = output.GetSemanticModel(tree);
            foreach (ClassDeclarationSyntax cls in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (cls.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
                {
                    continue;
                }

                INamedTypeSymbol? symbol = model.GetDeclaredSymbol(cls) as INamedTypeSymbol;
                if (symbol is null)
                {
                    continue;
                }

                checkedTypes++;
                if (!IsCovered(symbol))
                {
                    uncovered.Add(symbol.ToDisplayString());
                }
            }
        }

        Assert.True(checkedTypes > 0, "No generated (non-partial) types were found.");
        Assert.True(uncovered.Count == 0, "Generated types without ExcludeFromCodeCoverage: " + string.Join(", ", uncovered));
    }

    private static bool IsCovered(INamedTypeSymbol symbol)
    {
        for (INamedTypeSymbol? current = symbol; current is not null; current = current.ContainingType)
        {
            if (HasAttribute(current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAttribute(INamedTypeSymbol symbol)
    {
        return symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == AttributeName);
    }

    private static async Task<(Compilation Output, ImmutableArray<SyntaxTree> Generated)> RunGeneratorAsync(params string[] symbols)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: symbols);
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = framework.Concat<MetadataReference>(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        SyntaxTree input = CSharpSyntaxTree.ParseText(Source, parseOptions);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsCoverageAssembly",
            new[] { input },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        List<Diagnostic> errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, "Compilation errors: " + string.Join("; ", errors.Select(e => e.ToString())));

        ImmutableArray<SyntaxTree> generated = output.SyntaxTrees.Where(t => !ReferenceEquals(t, input)).ToImmutableArray();
        Assert.NotEmpty(generated);
        return (output, generated);
    }
}
