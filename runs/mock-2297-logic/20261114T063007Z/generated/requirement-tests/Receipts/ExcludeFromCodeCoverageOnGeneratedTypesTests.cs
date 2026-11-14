// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageOnGeneratedTypesTests
{
    private const string Source = """
        using MessagePack;

        namespace TestNs
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

            [Union(0, typeof(UnionA))]
            public interface IMyUnion
            {
            }

            [MessagePackObject]
            public class UnionA : IMyUnion
            {
                [Key(0)]
                public int C { get; set; }
            }

            [GeneratedMessagePackResolver]
            partial class MyResolver
            {
            }
        }
        """;

    [Fact]
    public async Task ObjectAndUnionFormatters_AreDecoratedWithExcludeFromCodeCoverage()
    {
        IReadOnlyList<SyntaxTree> trees = await RunGeneratorAsync();
        List<ClassDeclarationSyntax> formatters = trees
            .SelectMany(t => t.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Where(c => c.BaseList is not null && c.BaseList.ToString().Contains("IMessagePackFormatter"))
            .ToList();

        Assert.True(formatters.Count >= 3, $"Expected at least 3 generated formatters but found {formatters.Count}.");
        foreach (ClassDeclarationSyntax formatter in formatters)
        {
            Assert.True(HasExcludeFromCodeCoverage(formatter), $"Generated formatter '{formatter.Identifier.Text}' is not decorated with ExcludeFromCodeCoverage.");
        }
    }

    [Fact]
    public async Task GeneratedResolver_IsDecoratedWithExcludeFromCodeCoverage()
    {
        IReadOnlyList<SyntaxTree> trees = await RunGeneratorAsync();
        List<ClassDeclarationSyntax> resolvers = trees
            .SelectMany(t => t.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Where(c => c.Identifier.Text == "MyResolver" && c.BaseList is not null && c.BaseList.ToString().Contains("IFormatterResolver"))
            .ToList();

        Assert.NotEmpty(resolvers);
        foreach (ClassDeclarationSyntax resolver in resolvers)
        {
            Assert.True(HasExcludeFromCodeCoverage(resolver), "Generated resolver MyResolver is not decorated with ExcludeFromCodeCoverage.");
        }
    }

    private static bool HasExcludeFromCodeCoverage(ClassDeclarationSyntax declaration)
    {
        return declaration.AttributeLists
            .SelectMany(list => list.Attributes)
            .Any(a => a.Name.ToString().Contains("ExcludeFromCodeCoverage"));
    }

    private static async Task<IReadOnlyList<SyntaxTree>> RunGeneratorAsync(params string[] preprocessorSymbols)
    {
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3, preprocessorSymbols: preprocessorSymbols);
        ImmutableArray<MetadataReference> frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = frameworkReferences.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(Source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation _, out ImmutableArray<Diagnostic> _);
        IReadOnlyList<SyntaxTree> trees = driver.GetRunResult().GeneratedTrees;
        Assert.NotEmpty(trees);
        return trees;
    }
}
