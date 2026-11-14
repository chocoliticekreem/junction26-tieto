// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class CharacterizeEnumTemplateTests
{
    [Fact]
    public async Task EnumUsedByObject_GetsCompilingFormatterWithSerializeAndDeserialize()
    {
        string source = """
            using MessagePack;

            namespace MyNs
            {
                public enum MyEnum : byte
                {
                    A,
                    B,
                }

                [MessagePackObject]
                public class Holder
                {
                    [Key(0)]
                    public MyEnum E { get; set; }
                }

                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }
            }
            """;

        var (output, inputTree, generatorDiagnostics) = await RunGeneratorsAsync(source);

        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()));

        INamedTypeSymbol? target = output.GetTypeByMetadataName("MyNs.MyEnum");
        Assert.NotNull(target);
        INamedTypeSymbol formatter = Assert.Single(FindFormattersFor(output, inputTree, target!));
        Assert.NotEmpty(formatter.GetMembers("Serialize").OfType<IMethodSymbol>());
        Assert.NotEmpty(formatter.GetMembers("Deserialize").OfType<IMethodSymbol>());
    }

    private static List<INamedTypeSymbol> FindFormattersFor(Compilation output, SyntaxTree inputTree, INamedTypeSymbol target)
    {
        INamedTypeSymbol formatterInterface = output.GetTypeByMetadataName("MessagePack.Formatters.IMessagePackFormatter`1")!;
        List<INamedTypeSymbol> result = new();
        foreach (SyntaxTree tree in output.SyntaxTrees.Where(t => !ReferenceEquals(t, inputTree)))
        {
            SemanticModel model = output.GetSemanticModel(tree);
            foreach (TypeDeclarationSyntax decl in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(decl) is INamedTypeSymbol symbol &&
                    symbol.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, formatterInterface) && SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], target)) &&
                    !result.Contains(symbol, SymbolEqualityComparer.Default))
                {
                    result.Add(symbol);
                }
            }
        }

        return result;
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
