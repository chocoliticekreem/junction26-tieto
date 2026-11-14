// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageGeneratedCodeTests
{
    private const string Source = """
        using MessagePack;

        namespace Test
        {
            [MessagePackObject]
            public class MyObject
            {
                [Key(0)]
                public int A { get; set; }
            }

            [MessagePackObject(true)]
            public class MyMapObject
            {
                public int B { get; set; }
            }

            [Union(0, typeof(UnionImpl))]
            public interface IMyUnion { }

            [MessagePackObject]
            public class UnionImpl : IMyUnion
            {
                [Key(0)]
                public int C { get; set; }
            }

            [GeneratedMessagePackResolver]
            partial class MyResolver { }
        }
        """;

    [Fact]
    public async Task AllGeneratedTypesAreExcludedFromCodeCoverage()
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        SyntaxTree userTree = CSharpSyntaxTree.ParseText(Source, parseOptions);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { userTree },
            frameworkRefs.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new MessagePackGenerator().AsSourceGenerator() }, parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> _);

        var generatedTrees = output.SyntaxTrees.Where(t => !ReferenceEquals(t, userTree)).ToList();
        Assert.NotEmpty(generatedTrees);

        int checkedClasses = 0;
        foreach (SyntaxTree tree in generatedTrees)
        {
            SemanticModel model = output.GetSemanticModel(tree);
            foreach (ClassDeclarationSyntax decl in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                INamedTypeSymbol? symbol = model.GetDeclaredSymbol(decl);
                Assert.NotNull(symbol);
                bool hasAttribute = symbol!.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute");
                Assert.True(hasAttribute, $"Generated type '{symbol.ToDisplayString()}' in '{tree.FilePath}' lacks [ExcludeFromCodeCoverage].");
                checkedClasses++;
            }
        }

        Assert.True(checkedClasses >= 2, "Expected at least a resolver and a formatter to be generated.");
    }
}
