// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCoverageLegacyTargetsTests
{
    private const string Source = """
        using MessagePack;

        namespace ReceiptsSample
        {
            public enum Color { Red, Green }

            [MessagePackObject]
            public class IntKeyed
            {
                [Key(0)]
                public int Value { get; set; }

                [Key(1)]
                public Color Color { get; set; }
            }

            [MessagePackObject]
            public class StringKeyed
            {
                [Key("name")]
                public string Name { get; set; }
            }

            [Union(0, typeof(UnionMember))]
            public interface IUnionBase
            {
            }

            [MessagePackObject]
            public class UnionMember : IUnionBase
            {
                [Key(0)]
                public int X { get; set; }
            }
        }
        """;

    [Theory]
    [InlineData("NETSTANDARD2_0")]
    [InlineData("NETFRAMEWORK")]
    public async Task GeneratedCodeIsExcludedFromCoverageOnNetStandardAndNetFramework(string preprocessorSymbol)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest).WithPreprocessorSymbols(preprocessorSymbol);
        SyntaxTree inputTree = CSharpSyntaxTree.ParseText(Source, parseOptions);
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        IEnumerable<MetadataReference> references = framework.Concat<MetadataReference>(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsCoverageLegacyAssembly",
            new[] { inputTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new MessagePackGenerator().AsSourceGenerator() }, parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);

        var generatedTypes = new Dictionary<string, INamedTypeSymbol>();
        foreach (SyntaxTree tree in output.SyntaxTrees.Where(t => !ReferenceEquals(t, inputTree)))
        {
            SemanticModel model = output.GetSemanticModel(tree);
            foreach (TypeDeclarationSyntax decl in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(decl) is INamedTypeSymbol symbol && IsGeneratorOwned(symbol.Name))
                {
                    generatedTypes[symbol.ToDisplayString()] = symbol;
                }
            }
        }

        Assert.Contains(generatedTypes.Values, s => s.Name.EndsWith("Formatter", StringComparison.Ordinal));

        List<string> undecorated = generatedTypes.Values
            .Where(s => !s.GetAttributes().Any(a => a.AttributeClass?.Name == "ExcludeFromCodeCoverageAttribute"))
            .Select(s => s.ToDisplayString())
            .ToList();
        Assert.Empty(undecorated);
    }

    private static bool IsGeneratorOwned(string name)
        => name.EndsWith("Formatter", StringComparison.Ordinal)
        || name.EndsWith("Resolver", StringComparison.Ordinal)
        || name == "FormatterCache"
        || name.EndsWith("GetFormatterHelper", StringComparison.Ordinal);
}
