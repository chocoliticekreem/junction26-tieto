// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageTests
{
    [Fact]
    public async Task EveryGeneratedType_IsExcludedFromCodeCoverage()
    {
        string source = """
            using MessagePack;

            [MessagePackObject]
            public class IntKeyObject
            {
                [Key(0)]
                public int Value { get; set; }
            }

            [MessagePackObject(true)]
            public class StringKeyObject
            {
                public int Value { get; set; }
            }

            public enum SampleEnum
            {
                None,
                One,
            }

            [MessagePackObject]
            public class EnumContainer
            {
                [Key(0)]
                public SampleEnum Value { get; set; }
            }

            [MessagePackObject]
            [Union(0, typeof(DerivedUnion))]
            public abstract class UnionBase
            {
            }

            [MessagePackObject]
            public class DerivedUnion : UnionBase
            {
            }

            [GeneratedMessagePackResolver]
            public partial class TestResolver
            {
            }
            """;

        ImmutableArray<SyntaxTree> generatedTrees = await GenerateAsync(source);
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
                $"generated type {type.Identifier.ValueText} should be excluded from code coverage");
        }
    }

    private static bool IsExcludeFromCodeCoverage(AttributeSyntax attribute)
    {
        string name = attribute.Name.ToString();
        return name.EndsWith("ExcludeFromCodeCoverage", StringComparison.Ordinal)
            || name.EndsWith("ExcludeFromCodeCoverageAttribute", StringComparison.Ordinal);
    }

    private static async Task<ImmutableArray<SyntaxTree>> GenerateAsync(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3);
        var references = new List<MetadataReference>(
            await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None));
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: "Receipts.ExcludeFromCodeCoverage",
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
