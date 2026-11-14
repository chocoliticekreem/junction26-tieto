// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageDownlevelTargetsTests(ITestOutputHelper logger)
{
    private const string AttributeName = "System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute";

    private const string Source = """
        using MessagePack;

        namespace TestNs
        {
            [GeneratedMessagePackResolver]
            partial class MyResolver
            {
            }

            [MessagePackObject]
            public class IntKeyed
            {
                [Key(0)]
                public int A { get; set; }
            }

            [MessagePackObject(true)]
            public class StringKeyed
            {
                public int B { get; set; }
            }
        }
        """;

    [Theory]
    [InlineData("NETSTANDARD2_0")]
    [InlineData("NETFRAMEWORK")]
    public async Task GeneratedCodeIsExcludedEvenWhenDownlevelSymbolDefined(string symbol)
    {
        // The attribute exists on these downlevel targets, so the requirement's "all generated code" applies there too.
        ReferenceAssemblies downlevel = symbol == "NETSTANDARD2_0" ? ReferenceAssemblies.NetStandard.NetStandard20 : ReferenceAssemblies.NetFramework.Net472.Default;
        ImmutableArray<MetadataReference> downlevelRefs = await downlevel.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        CSharpCompilation probe = CSharpCompilation.Create("Probe", Array.Empty<SyntaxTree>(), downlevelRefs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.NotNull(probe.GetTypeByMetadataName(AttributeName));

        ImmutableArray<MetadataReference> frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = new List<MetadataReference>(frameworkRefs);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: new[] { symbol });
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsDownlevelAssembly",
            new[] { CSharpSyntaxTree.ParseText(Source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> _);
        Assert.All(driver.GetRunResult().Results, r => Assert.Null(r.Exception));

        ImmutableArray<SyntaxTree> generatedTrees = output.SyntaxTrees.Where(t => !compilation.SyntaxTrees.Contains(t)).ToImmutableArray();
        Assert.NotEmpty(generatedTrees);

        var generatedSet = new HashSet<SyntaxTree>(generatedTrees);
        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var offenders = new List<string>();
        foreach (SyntaxTree tree in generatedTrees)
        {
            SemanticModel model = output.GetSemanticModel(tree);
            foreach (BaseTypeDeclarationSyntax decl in tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(decl) is not INamedTypeSymbol type || !seen.Add(type))
                {
                    continue;
                }

                bool fullyGenerated = type.DeclaringSyntaxReferences.All(r => generatedSet.Contains(r.SyntaxTree));
                if (fullyGenerated && !IsExcluded(type))
                {
                    offenders.Add(type.ToDisplayString());
                    logger.WriteLine("Not excluded from coverage under " + symbol + ": " + type.ToDisplayString());
                }
            }
        }

        Assert.Empty(offenders);
    }

    private static bool IsExcluded(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (current.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == AttributeName))
            {
                return true;
            }
        }

        return false;
    }
}
