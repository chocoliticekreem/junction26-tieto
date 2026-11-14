// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageGeneratedTests
{
    private const string ExcludeAttributeName = "System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute";

    private const string Source = """
        using MessagePack;

        namespace TestNs
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
                public int B { get; set; }
            }

            [Union(0, typeof(UnionCase))]
            public interface IUnion
            {
            }

            [MessagePackObject]
            public class UnionCase : IUnion
            {
                [Key(0)]
                public int X { get; set; }
            }
        }
        """;

    [Fact]
    public async Task GeneratedFormattersAreDecoratedWithExcludeFromCodeCoverage()
    {
        (Compilation output, List<SyntaxTree> generated) = await RunGeneratorAsync();
        Assert.NotEmpty(generated);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        List<string> formatterNames = new();
        foreach (SyntaxTree tree in generated)
        {
            SemanticModel model = output.GetSemanticModel(tree);
            foreach (ClassDeclarationSyntax decl in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (!decl.Identifier.ValueText.EndsWith("Formatter", StringComparison.Ordinal))
                {
                    continue;
                }

                INamedTypeSymbol? symbol = model.GetDeclaredSymbol(decl);
                Assert.NotNull(symbol);
                formatterNames.Add(symbol!.Name);
                Assert.True(HasExcludeAttribute(symbol), $"Generated formatter {symbol.Name} is not decorated with ExcludeFromCodeCoverage.");
            }
        }

        Assert.Contains(formatterNames, n => n.Contains("IntKeyed"));
        Assert.Contains(formatterNames, n => n.Contains("StringKeyed"));
        Assert.Contains(formatterNames, n => n.Contains("IUnion"));
    }

    [Fact]
    public async Task GeneratedResolverIsDecoratedWithExcludeFromCodeCoverage()
    {
        (Compilation output, List<SyntaxTree> generated) = await RunGeneratorAsync();
        Assert.NotEmpty(generated);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        INamedTypeSymbol? resolver = output.GetTypeByMetadataName("TestNs.MyResolver");
        Assert.NotNull(resolver);
        Assert.True(HasExcludeAttribute(resolver!), "Generated resolver is not decorated with ExcludeFromCodeCoverage.");
    }

    private static bool HasExcludeAttribute(INamedTypeSymbol symbol)
    {
        return symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == ExcludeAttributeName);
    }

    private static async Task<(Compilation Output, List<SyntaxTree> Generated)> RunGeneratorAsync()
    {
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = new List<MetadataReference>(framework);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsExcludeFromCodeCoverage",
            new[] { CSharpSyntaxTree.ParseText(Source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator()).WithUpdatedParseOptions(parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> _);
        List<SyntaxTree> generated = output.SyntaxTrees.Where(t => !compilation.SyntaxTrees.Contains(t)).ToList();
        return (output, generated);
    }
}
