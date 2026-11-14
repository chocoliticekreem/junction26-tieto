// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageNetStandardTests(ITestOutputHelper logger)
{
    private const string AttributeName = "System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute";

    private const string Source = """
        using MessagePack;

        namespace MyNs
        {
            [GeneratedMessagePackResolver]
            partial class MyResolver { }

            [MessagePackObject]
            public class IntKeyed
            {
                [Key(0)] public int A { get; set; }
            }
        }
        """;

    [Fact]
    public async Task GeneratedTypesExcludedWhenTargetingNetStandard20()
    {
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3, preprocessorSymbols: new[] { "NETSTANDARD2_0", "NETSTANDARD" });
        ImmutableArray<MetadataReference> frameworkRefs = await ReferenceAssemblies.NetStandard.NetStandard20.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        IEnumerable<MetadataReference> allRefs = Enumerable.Concat<MetadataReference>(frameworkRefs, ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsNetStandardAssembly",
            new[] { CSharpSyntaxTree.ParseText(Source, parseOptions, path: "Source.cs") },
            allRefs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // The attribute exists on netstandard2.0, so there is no reason to omit it there.
        Assert.NotNull(compilation.GetTypeByMetadataName(AttributeName));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);

        foreach (Diagnostic d in generatorDiagnostics)
        {
            logger.WriteLine("generator: " + d);
        }

        List<SyntaxTree> generatedTrees = output.SyntaxTrees.Where(t => !compilation.SyntaxTrees.Contains(t)).ToList();
        Assert.NotEmpty(generatedTrees);

        HashSet<string> checkedTypes = new HashSet<string>();
        HashSet<string> missing = new HashSet<string>();
        foreach (SyntaxTree tree in generatedTrees)
        {
            SemanticModel model = output.GetSemanticModel(tree);
            foreach (BaseTypeDeclarationSyntax decl in tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(decl) is not INamedTypeSymbol symbol)
                {
                    continue;
                }

                bool fullyGenerated = symbol.DeclaringSyntaxReferences.All(r => generatedTrees.Contains(r.SyntaxTree));
                bool isResolver = symbol.Name == "MyResolver";
                if (!fullyGenerated && !isResolver)
                {
                    continue;
                }

                string name = symbol.ToDisplayString();
                checkedTypes.Add(name);
                if (!symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == AttributeName))
                {
                    missing.Add(name);
                }
            }
        }

        foreach (string t in checkedTypes)
        {
            logger.WriteLine("checked: " + t);
        }

        Assert.Contains("MyNs.MyResolver", checkedTypes);
        Assert.True(missing.Count == 0, "generated types without ExcludeFromCodeCoverage under netstandard2.0: " + string.Join(", ", missing));
    }
}
