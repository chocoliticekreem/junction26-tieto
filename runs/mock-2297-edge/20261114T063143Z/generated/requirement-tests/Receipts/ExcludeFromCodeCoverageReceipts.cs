// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageReceipts
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
        [Key(1)] public MyEnum E { get; set; }
    }

    [MessagePackObject(true)]
    public class StringKeyObject
    {
        public string B { get; set; }
    }

    [Union(0, typeof(UnionMember))]
    public interface IMyUnion { }

    [MessagePackObject]
    public class UnionMember : IMyUnion
    {
        [Key(0)] public int C { get; set; }
    }

    public enum MyEnum { A, B }
}
";

    [Fact]
    public async Task GeneratedFormattersAndResolvers_AreExcludedFromCodeCoverage()
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3);
        SyntaxTree userTree = CSharpSyntaxTree.ParseText(Source, parseOptions);
        var frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = Enumerable.Concat<MetadataReference>(frameworkRefs, ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        Compilation compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { userTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new global::MessagePack.SourceGenerator.MessagePackGenerator()).WithUpdatedParseOptions(parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);

        int checkedCount = 0;
        var uncovered = new List<string>();
        foreach (SyntaxTree tree in output.SyntaxTrees)
        {
            if (tree == userTree || tree.FilePath == userTree.FilePath && tree.ToString() == userTree.ToString())
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

        Assert.True(checkedCount >= 2, $"Expected generated formatters and a resolver, found {checkedCount}.");
        Assert.True(uncovered.Count == 0, "Generated types without [ExcludeFromCodeCoverage]: " + string.Join(", ", uncovered));
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
