// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class CharacterizeEmitNestingTypesAndNamespacesTests
{
    [Fact]
    public async Task FormatterForTypeInDeepNamespace_IsNestedInsideResolver()
    {
        string source = """
            using MessagePack;

            namespace A.B.C
            {
                [MessagePackObject]
                public class MyObject
                {
                    [Key(0)]
                    public int Value { get; set; }
                }
            }

            namespace MyNs
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }
            }
            """;

        await AssertFormatterNestedInResolverAsync(source, "A.B.C.MyObject");
    }

    [Fact]
    public async Task FormatterForNestedDataType_IsNestedInsideResolver()
    {
        string source = """
            using MessagePack;

            namespace MyNs
            {
                public class Outer
                {
                    [MessagePackObject]
                    public class Inner
                    {
                        [Key(0)]
                        public int Value { get; set; }
                    }
                }

                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }
            }
            """;

        await AssertFormatterNestedInResolverAsync(source, "MyNs.Outer+Inner");
    }

    private static async Task AssertFormatterNestedInResolverAsync(string source, string dataTypeMetadataName)
    {
        var (output, inputTree, generatorDiagnostics) = await RunGeneratorsAsync(source);

        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()));

        INamedTypeSymbol? target = output.GetTypeByMetadataName(dataTypeMetadataName);
        INamedTypeSymbol? resolver = output.GetTypeByMetadataName("MyNs.MyResolver");
        Assert.NotNull(target);
        Assert.NotNull(resolver);

        INamedTypeSymbol formatterInterface = output.GetTypeByMetadataName("MessagePack.Formatters.IMessagePackFormatter`1")!;
        List<INamedTypeSymbol> formatters = new();
        foreach (SyntaxTree tree in output.SyntaxTrees.Where(t => !ReferenceEquals(t, inputTree)))
        {
            SemanticModel model = output.GetSemanticModel(tree);
            foreach (TypeDeclarationSyntax decl in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(decl) is INamedTypeSymbol symbol &&
                    symbol.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, formatterInterface) && SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], target)) &&
                    !formatters.Contains(symbol, SymbolEqualityComparer.Default))
                {
                    formatters.Add(symbol);
                }
            }
        }

        INamedTypeSymbol formatter = Assert.Single(formatters);
        bool nestedInResolver = false;
        for (INamedTypeSymbol? container = formatter.ContainingType; container is not null; container = container.ContainingType)
        {
            if (SymbolEqualityComparer.Default.Equals(container, resolver))
            {
                nestedInResolver = true;
                break;
            }
        }

        Assert.True(nestedInResolver, $"Formatter {formatter.ToDisplayString()} is not nested in MyNs.MyResolver.");
    }

    private static async Task<(Compilation Output, SyntaxTree InputTree, ImmutableArray<Diagnostic> GeneratorDiagnostics)> RunGeneratorsAsync(string source)
    {
        ImmutableArray<MetadataReference> frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        IEnumerable<MetadataReference> allRefs = frameworkRefs.Concat<MetadataReference>(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToArray();
        CSharpParseOptions parseOptions = new(LanguageVersion.Latest);
        SyntaxTree inputTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { inputTree },
            allRefs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        List<ISourceGenerator> generators = new();
        foreach (Type t in typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly.GetTypes())
        {
            if (t.IsAbstract || t.GetCustomAttributes(typeof(GeneratorAttribute), false).Length == 0)
            {
                continue;
            }

            object instance = Activator.CreateInstance(t, nonPublic: true)!;
            if (instance is IIncrementalGenerator incremental)
            {
                generators.Add(incremental.AsSourceGenerator());
            }
            else if (instance is ISourceGenerator sourceGenerator)
            {
                generators.Add(sourceGenerator);
            }
        }

        GeneratorDriver driver = CSharpGeneratorDriver.Create(generators, parseOptions: parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
        return (output, inputTree, diagnostics);
    }
}
