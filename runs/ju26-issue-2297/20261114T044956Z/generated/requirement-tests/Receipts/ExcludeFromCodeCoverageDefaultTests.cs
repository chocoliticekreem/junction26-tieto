// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageDefaultTests
{
    private const string Source = """
        using MessagePack;

        namespace MyNs
        {
            [MessagePackObject]
            public class MyObject
            {
                [Key(0)]
                public int Value { get; set; }
            }

            public enum MyEnum { A, B }

            [MessagePackObject]
            public class Holder
            {
                [Key(0)]
                public MyEnum E { get; set; }

                [Key(1)]
                public MyObject? O { get; set; }
            }

            [GeneratedMessagePackResolver]
            partial class MyResolver
            {
            }
        }
        """;

    [Fact]
    public async Task GeneratedResolver_IsDecoratedWithExcludeFromCodeCoverage()
    {
        (Compilation output, SyntaxTree input) = await RunGeneratorAsync(CSharpParseOptions.Default);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        INamedTypeSymbol? resolver = output.GetTypeByMetadataName("MyNs.MyResolver");
        Assert.NotNull(resolver);
        Assert.True(HasExcludeFromCodeCoverage(resolver!), "Generated resolver MyNs.MyResolver is not decorated with ExcludeFromCodeCoverage.");
    }

    [Fact]
    public async Task GeneratedFormatters_AreDecoratedWithExcludeFromCodeCoverage()
    {
        (Compilation output, SyntaxTree input) = await RunGeneratorAsync(CSharpParseOptions.Default);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        List<INamedTypeSymbol> formatters = FindGeneratedFormatters(output, input);
        Assert.True(formatters.Count >= 2, $"Expected at least 2 generated formatters but found {formatters.Count}.");
        foreach (INamedTypeSymbol formatter in formatters)
        {
            Assert.True(HasExcludeFromCodeCoverage(formatter), $"Generated formatter {formatter.ToDisplayString()} is not decorated with ExcludeFromCodeCoverage.");
        }
    }

    private static bool HasExcludeFromCodeCoverage(INamedTypeSymbol symbol)
    {
        return symbol.GetAttributes().Any(a => a.AttributeClass?.Name == "ExcludeFromCodeCoverageAttribute");
    }

    private static List<INamedTypeSymbol> FindGeneratedFormatters(Compilation output, SyntaxTree input)
    {
        var formatters = new List<INamedTypeSymbol>();
        foreach (SyntaxTree tree in output.SyntaxTrees)
        {
            if (tree == input)
            {
                continue;
            }

            SemanticModel model = output.GetSemanticModel(tree);
            foreach (ClassDeclarationSyntax decl in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(decl) is INamedTypeSymbol type && type.AllInterfaces.Any(i => i.Name == "IMessagePackFormatter"))
                {
                    formatters.Add(type);
                }
            }
        }

        return formatters;
    }

    private static async Task<(Compilation Output, SyntaxTree Input)> RunGeneratorAsync(CSharpParseOptions parseOptions)
    {
        var references = new List<MetadataReference>();
        references.AddRange(await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None));
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        SyntaxTree input = CSharpSyntaxTree.ParseText(Source, parseOptions);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { input },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
        return (output, input);
    }
}
