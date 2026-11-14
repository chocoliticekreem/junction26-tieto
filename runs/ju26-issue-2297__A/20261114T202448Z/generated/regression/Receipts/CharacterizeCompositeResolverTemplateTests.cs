// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class CharacterizeCompositeResolverTemplateTests
{
    private const string Source = """
        using MessagePack;

        namespace MyNs
        {
            [CompositeResolver(typeof(MessagePack.Resolvers.StandardResolver))]
            public partial class MyCompositeResolver
            {
            }
        }
        """;

    [Fact]
    public async Task CompositeResolver_GeneratesCompilingResolverImplementingIFormatterResolver()
    {
        var (output, inputTree, generatorDiagnostics) = await RunGeneratorsAsync(Source);

        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()));

        INamedTypeSymbol? resolver = output.GetTypeByMetadataName("MyNs.MyCompositeResolver");
        INamedTypeSymbol? resolverInterface = output.GetTypeByMetadataName("MessagePack.IFormatterResolver");
        Assert.NotNull(resolver);
        Assert.NotNull(resolverInterface);
        Assert.Contains(resolver!.AllInterfaces, i => SymbolEqualityComparer.Default.Equals(i, resolverInterface));

        IFieldSymbol instance = Assert.Single(resolver.GetMembers("Instance").OfType<IFieldSymbol>());
        Assert.True(instance.IsStatic);
        Assert.True(SymbolEqualityComparer.Default.Equals(instance.Type, resolver));
        Assert.NotEmpty(resolver.GetMembers("GetFormatter").OfType<IMethodSymbol>());
    }

    [Fact]
    public async Task CompositeResolver_GeneratedSourceReferencesListedResolver()
    {
        var (output, inputTree, _) = await RunGeneratorsAsync(Source);

        SyntaxTree[] declaringTrees = output.SyntaxTrees
            .Where(t => !ReferenceEquals(t, inputTree))
            .Where(t => t.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>().Any(d => d.Identifier.ValueText == "MyCompositeResolver"))
            .ToArray();

        Assert.NotEmpty(declaringTrees);
        Assert.Contains(declaringTrees, t => t.ToString().Contains("StandardResolver"));
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
