// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageDownlevelTargetTests
{
    private const string AttributeName = "System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute";

    private const string Source = """
        using MessagePack;

        namespace Receipts.Sample
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
        }
        """;

    // ExcludeFromCodeCoverageAttribute exists (and targets classes) in netstandard2.0 and .NET Framework 4.0+,
    // so the requirement 'all generated code is decorated' applies to those targets too.
    [Theory]
    [InlineData("NETSTANDARD2_0")]
    [InlineData("NETFRAMEWORK")]
    public async Task GeneratedTypesAreExcludedWhenTargetSymbolIsDefined(string preprocessorSymbol)
    {
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3, preprocessorSymbols: new[] { preprocessorSymbol });

        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = new List<MetadataReference>(framework);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        SyntaxTree sourceTree = CSharpSyntaxTree.ParseText(Source, parseOptions);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsDownlevel",
            new[] { sourceTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);

        Assert.All(driver.GetRunResult().Results, r => Assert.Null(r.Exception));

        List<INamedTypeSymbol> types = new List<INamedTypeSymbol>();
        foreach (SyntaxTree tree in output.SyntaxTrees.Where(t => t != sourceTree))
        {
            SemanticModel model = output.GetSemanticModel(tree);
            foreach (BaseTypeDeclarationSyntax declaration in tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(declaration) is INamedTypeSymbol symbol
                    && symbol.GetMembers().Any(m => m is not INamedTypeSymbol && !m.IsImplicitlyDeclared && m.DeclaringSyntaxReferences.Any(r => r.SyntaxTree == tree))
                    && !types.Contains(symbol, SymbolEqualityComparer.Default))
                {
                    types.Add(symbol);
                }
            }
        }

        Assert.Contains(types, t => t.Name.EndsWith("Formatter", StringComparison.Ordinal));
        List<string> missing = types
            .Where(t => !t.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == AttributeName))
            .Select(t => t.ToDisplayString())
            .ToList();
        Assert.Empty(missing);
    }
}
