// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageTargetCoverageTests
{
    private const string Source = """
        using MessagePack;

        namespace TestNs
        {
            public enum MyEnum
            {
                One,
                Two,
            }

            [MessagePackObject]
            public class MyObject
            {
                [Key(0)]
                public int A { get; set; }

                [Key(1)]
                public MyEnum E { get; set; }
            }

            [GeneratedMessagePackResolver]
            partial class MyResolver
            {
            }
        }
        """;

    [Fact]
    public async Task EnumFormatter_IsDecoratedWithExcludeFromCodeCoverage()
    {
        IReadOnlyList<SyntaxTree> trees = await RunGeneratorAsync();
        List<ClassDeclarationSyntax> enumFormatters = FormatterClasses(trees)
            .Where(c => c.BaseList!.ToString().Contains("MyEnum"))
            .ToList();

        Assert.NotEmpty(enumFormatters);
        foreach (ClassDeclarationSyntax formatter in enumFormatters)
        {
            Assert.True(HasExcludeFromCodeCoverage(formatter), $"Generated enum formatter '{formatter.Identifier.Text}' is not decorated with ExcludeFromCodeCoverage.");
        }
    }

    [Fact]
    public async Task ObjectFormatter_IsDecoratedWhenTargetingNetStandard20()
    {
        IReadOnlyList<SyntaxTree> trees = await RunGeneratorAsync("NETSTANDARD2_0");
        List<ClassDeclarationSyntax> objectFormatters = FormatterClasses(trees)
            .Where(c => c.BaseList!.ToString().Contains("MyObject"))
            .ToList();

        Assert.NotEmpty(objectFormatters);
        foreach (ClassDeclarationSyntax formatter in objectFormatters)
        {
            Assert.True(HasExcludeFromCodeCoverage(formatter), $"Generated formatter '{formatter.Identifier.Text}' is not decorated with ExcludeFromCodeCoverage when NETSTANDARD2_0 is defined.");
        }
    }

    private static List<ClassDeclarationSyntax> FormatterClasses(IEnumerable<SyntaxTree> trees)
    {
        return trees
            .SelectMany(t => t.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Where(c => c.BaseList is not null && c.BaseList.ToString().Contains("IMessagePackFormatter"))
            .ToList();
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
            "ReceiptsTargetTestAssembly",
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
