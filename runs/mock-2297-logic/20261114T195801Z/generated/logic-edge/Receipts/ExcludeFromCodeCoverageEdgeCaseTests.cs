// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageEdgeCaseTests(ITestOutputHelper logger)
{
    private const string DataSource = """
        using MessagePack;

        namespace TestNs
        {
            public enum MyEnum { A, B }

            [MessagePackObject]
            public class MyObject
            {
                [Key(0)]
                public MyEnum Value { get; set; }
            }

            [GeneratedMessagePackResolver]
            partial class MyResolver { }
        }
        """;

    private const string UserExcludedResolverSource = """
        using MessagePack;

        namespace TestNs
        {
            [MessagePackObject]
            public class MyObject
            {
                [Key(0)]
                public int Value { get; set; }
            }

            [GeneratedMessagePackResolver]
            [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
            partial class MyResolver { }
        }
        """;

    [Fact]
    public async Task EnumFormatter_HasAttribute_OnModernTarget()
    {
        Compilation output = await this.RunGeneratorAsync(DataSource, "NET8_0", "NETCOREAPP");
        INamedTypeSymbol formatter = FindFormatter(output, "MyEnum");
        Assert.True(HasExcludeAttribute(formatter), "Enum formatter should be decorated with ExcludeFromCodeCoverage on net8.0.");
    }

    [Theory]
    [InlineData("NETFRAMEWORK")]
    [InlineData("NETSTANDARD2_0")]
    public async Task ObjectFormatter_HasAttribute_OnLegacyTargets(string symbol)
    {
        Compilation output = await this.RunGeneratorAsync(DataSource, symbol);
        INamedTypeSymbol formatter = FindFormatter(output, "MyObject");
        Assert.True(HasExcludeAttribute(formatter), $"Object formatter should be decorated with ExcludeFromCodeCoverage when {symbol} is defined (the attribute exists there).");
    }

    [Fact]
    public async Task ResolverAlreadyExcludedByUser_StillCompiles()
    {
        Compilation output = await this.RunGeneratorAsync(UserExcludedResolverSource, "NET8_0", "NETCOREAPP");
        List<Diagnostic> errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        foreach (Diagnostic d in errors)
        {
            logger.WriteLine(d.ToString());
        }

        Assert.Empty(errors);
    }

    private static bool HasExcludeAttribute(INamedTypeSymbol symbol)
    {
        return symbol.GetAttributes().Any(a => a.AttributeClass?.Name == "ExcludeFromCodeCoverageAttribute");
    }

    private static INamedTypeSymbol FindFormatter(Compilation output, string dataTypeName)
    {
        foreach (SyntaxTree tree in output.SyntaxTrees)
        {
            SemanticModel model = output.GetSemanticModel(tree);
            foreach (ClassDeclarationSyntax cls in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(cls) is INamedTypeSymbol symbol &&
                    symbol.AllInterfaces.Any(i => i.Name == "IMessagePackFormatter" && i.TypeArguments.Length == 1 && i.TypeArguments[0].Name == dataTypeName))
                {
                    return symbol;
                }
            }
        }

        throw new Xunit.Sdk.XunitException($"No generated formatter found for {dataTypeName}.");
    }

    private async Task<Compilation> RunGeneratorAsync(string source, params string[] preprocessorSymbols)
    {
        CSharpParseOptions parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Latest)
            .WithPreprocessorSymbols(preprocessorSymbols);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, parseOptions);

        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = new List<MetadataReference>(framework);
        foreach (var r in ReferencesHelper.GetReferences(ReferencesSet.MessagePack))
        {
            references.Add(r);
        }

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);

        foreach (Diagnostic d in generatorDiagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        foreach (SyntaxTree t in output.SyntaxTrees)
        {
            if (t != tree)
            {
                logger.WriteLine("// " + t.FilePath);
                logger.WriteLine(t.ToString());
            }
        }

        return output;
    }
}
