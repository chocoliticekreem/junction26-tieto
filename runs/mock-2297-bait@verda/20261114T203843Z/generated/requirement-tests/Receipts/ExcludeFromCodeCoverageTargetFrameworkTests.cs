// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageTargetFrameworkTests
{
    [Theory]
    [InlineData("NETSTANDARD2_0")]
    [InlineData("NETFRAMEWORK")]
    public async Task GeneratedTypes_RemainExcludedWhenTargetFrameworkSymbolsAreDefined(string targetFrameworkSymbol)
    {
        await VerifyAttributeExistsOnNetStandard20Async();

        string source = """
            using MessagePack;

            [MessagePackObject]
            public class SampleObject
            {
                [Key(0)]
                public int Value { get; set; }
            }

            [GeneratedMessagePackResolver]
            public partial class TestResolver
            {
            }
            """;

        ImmutableArray<SyntaxTree> generatedTrees = await GenerateAsync(source, targetFrameworkSymbol);
        var generatedTypes = new List<TypeDeclarationSyntax>();
        foreach (SyntaxTree generatedTree in generatedTrees)
        {
            SyntaxNode root = await generatedTree.GetRootAsync();
            generatedTypes.AddRange(root.DescendantNodes().OfType<TypeDeclarationSyntax>());
        }

        generatedTypes.Should().NotBeEmpty();
        foreach (TypeDeclarationSyntax type in generatedTypes)
        {
            type.AttributeLists.SelectMany(list => list.Attributes).Should().Contain(
                attribute => IsExcludeFromCodeCoverage(attribute),
                $"generated type {type.Identifier.ValueText} should remain excluded when {targetFrameworkSymbol} is defined");
        }
    }

    private static async Task VerifyAttributeExistsOnNetStandard20Async()
    {
        var references = await ReferenceAssemblies.Net.NetStandard20.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        CSharpCompilation probe = CSharpCompilation.Create(
            assemblyName: "Receipts.ExcludeAttributeProbe",
            syntaxTrees: new[]
            {
                CSharpSyntaxTree.ParseText("[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] internal class Probe { }"),
            },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        probe.GetDiagnostics().Should().NotContain(
            diagnostic => diagnostic.Severity == DiagnosticSeverity.Error,
            "ExcludeFromCodeCoverageAttribute is available to netstandard2.0 consumers");
    }

    private static bool IsExcludeFromCodeCoverage(AttributeSyntax attribute)
    {
        string name = attribute.Name.ToString();
        return name.EndsWith("ExcludeFromCodeCoverage", StringComparison.Ordinal)
            || name.EndsWith("ExcludeFromCodeCoverageAttribute", StringComparison.Ordinal);
    }

    private static async Task<ImmutableArray<SyntaxTree>> GenerateAsync(string source, params string[] preprocessorSymbols)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3, preprocessorSymbols: preprocessorSymbols);
        var references = new List<MetadataReference>(
            await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None));
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: "Receipts.ExcludeFromCodeCoverageTargets",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            additionalTexts: null,
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> generatorDiagnostics);

        generatorDiagnostics.Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        outputCompilation.GetDiagnostics().Should().NotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        return driver.GetRunResult().GeneratedTrees;
    }
}
