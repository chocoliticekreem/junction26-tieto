// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageDownlevelTargetTests
{
    private const string AttributeMetadataName = "System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute";

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

            [MessagePackObject]
            public class Holder
            {
                [Key(0)]
                public MyObject? O { get; set; }
            }

            [GeneratedMessagePackResolver]
            partial class MyResolver
            {
            }
        }
        """;

    [Fact]
    public async Task GeneratedResolver_IsDecorated_WhenNetStandard20SymbolDefined()
    {
        // Precondition: the attribute exists on these targets, so the requirement asks for it there too.
        var netStandardRefs = await ReferenceAssemblies.NetStandard.NetStandard20.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        Assert.NotNull(CSharpCompilation.Create("ns20", references: netStandardRefs).GetTypeByMetadataName(AttributeMetadataName));
        var netFrameworkRefs = await ReferenceAssemblies.NetFramework.Net472.Default.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        Assert.NotNull(CSharpCompilation.Create("net472", references: netFrameworkRefs).GetTypeByMetadataName(AttributeMetadataName));

        (Compilation output, SyntaxTree input) = await RunGeneratorAsync(CSharpParseOptions.Default.WithPreprocessorSymbols("NETSTANDARD2_0"));
        INamedTypeSymbol? resolver = output.GetTypeByMetadataName("MyNs.MyResolver");
        Assert.NotNull(resolver);
        Assert.True(HasExcludeFromCodeCoverage(resolver!), "Generated resolver is not decorated with ExcludeFromCodeCoverage when NETSTANDARD2_0 is defined.");
    }

    [Fact]
    public async Task GeneratedFormatters_AreDecorated_WhenNetFrameworkSymbolDefined()
    {
        (Compilation output, SyntaxTree input) = await RunGeneratorAsync(CSharpParseOptions.Default.WithPreprocessorSymbols("NETFRAMEWORK"));
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

        Assert.NotEmpty(formatters);
        foreach (INamedTypeSymbol formatter in formatters)
        {
            Assert.True(HasExcludeFromCodeCoverage(formatter), $"Generated formatter {formatter.ToDisplayString()} is not decorated with ExcludeFromCodeCoverage when NETFRAMEWORK is defined.");
        }
    }

    private static bool HasExcludeFromCodeCoverage(INamedTypeSymbol symbol)
    {
        return symbol.GetAttributes().Any(a => a.AttributeClass?.Name == "ExcludeFromCodeCoverageAttribute");
    }

    private static async Task<(Compilation Output, SyntaxTree Input)> RunGeneratorAsync(CSharpParseOptions parseOptions)
    {
        var references = new List<MetadataReference>();
        references.AddRange(await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None));
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        SyntaxTree input = CSharpSyntaxTree.ParseText(Source, parseOptions);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsDownlevelAssembly",
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
