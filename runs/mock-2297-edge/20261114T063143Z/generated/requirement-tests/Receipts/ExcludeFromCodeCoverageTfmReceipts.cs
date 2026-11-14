// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageTfmReceipts
{
    private const string Source = @"
using MessagePack;

namespace TestNs
{
    [GeneratedMessagePackResolver]
    partial class MyResolver { }

    [MessagePackObject]
    public class IntKeyObject
    {
        [Key(0)] public int A { get; set; }
    }
}
";

    // ExcludeFromCodeCoverageAttribute exists on .NET Framework 4.0+ and netstandard2.0,
    // so generated code for those targets should be decorated too.
    [Theory]
    [InlineData("NETSTANDARD2_0")]
    [InlineData("NETFRAMEWORK")]
    public async Task GeneratedTypes_AreExcludedFromCoverage_WhenLegacySymbolDefined(string symbolName)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3, preprocessorSymbols: new[] { symbolName });
        SyntaxTree userTree = CSharpSyntaxTree.ParseText(Source, parseOptions);
        var frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = Enumerable.Concat<MetadataReference>(frameworkRefs, ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        Compilation compilation = CSharpCompilation.Create(
            "ReceiptsTfmTestAssembly",
            new[] { userTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new global::MessagePack.SourceGenerator.MessagePackGenerator()).WithUpdatedParseOptions(parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);

        int checkedCount = 0;
        var uncovered = new List<string>();
        foreach (SyntaxTree tree in output.SyntaxTrees)
        {
            if (tree == userTree)
            {
                continue;
            }

            SemanticModel model = output.GetSemanticModel(tree);
            foreach (var decl in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(decl) is not INamedTypeSymbol symbol)
                {
                    continue;
                }

                bool isFormatter = symbol.AllInterfaces.Any(i => i.Name == "IMessagePackFormatter");
                bool isResolver = symbol.AllInterfaces.Any(i => i.Name == "IFormatterResolver");
                if (!isFormatter && !isResolver)
                {
                    continue;
                }

                checkedCount++;
                if (!IsCovered(symbol))
                {
                    uncovered.Add(symbol.ToDisplayString());
                }
            }
        }

        Assert.True(checkedCount >= 2, $"Expected a generated formatter and resolver, found {checkedCount}.");
        Assert.True(uncovered.Count == 0, $"With {symbolName} defined, generated types lack [ExcludeFromCodeCoverage]: " + string.Join(", ", uncovered));
    }

    private static bool IsCovered(INamedTypeSymbol? symbol)
    {
        for (INamedTypeSymbol? current = symbol; current is not null; current = current.ContainingType)
        {
            if (current.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute"))
            {
                return true;
            }
        }

        return false;
    }
}
