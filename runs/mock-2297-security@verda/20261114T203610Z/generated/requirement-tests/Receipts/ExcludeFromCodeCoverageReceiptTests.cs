// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageReceiptTests
{
    [Fact]
    public void Generated_formatters_and_resolver_carry_coverage_exclusion()
    {
        const string source = """
            using MessagePack;

            [MessagePackObject]
            public class Sample
            {
                [Key(0)]
                public int Value { get; set; }
            }

            [GeneratedMessagePackResolver]
            public partial class TestResolver
            {
            }
            """;

        ImmutableArray<ClassDeclarationSyntax> generatedTypes = GenerateTypes(source);

        AssertAllDecorated(generatedTypes);
    }

    [Fact]
    public void Generated_string_key_enum_and_union_formatters_carry_coverage_exclusion()
    {
        const string source = """
            using MessagePack;

            public enum SampleEnum
            {
                None,
                Value,
            }

            [MessagePackObject]
            public class Root
            {
                [Key(0)]
                public SampleEnum EnumValue { get; set; }

                [Key(1)]
                public UnionBase UnionValue { get; set; }
            }

            [MessagePackObject]
            public class StringKeyed
            {
                [Key("name")]
                public string Name { get; set; }
            }

            [MessagePackObject]
            [Union(0, typeof(UnionDerived))]
            public abstract class UnionBase
            {
            }

            [MessagePackObject]
            public class UnionDerived : UnionBase
            {
                [Key(0)]
                public int Id { get; set; }
            }
            """;

        ImmutableArray<ClassDeclarationSyntax> generatedTypes = GenerateTypes(source);

        Assert.True(generatedTypes.Length >= 5, $"Expected formatters for the object, string-keyed object, enum, union, and union member, but found only {generatedTypes.Length} generated classes.");
        AssertAllDecorated(generatedTypes);
    }

    private static ImmutableArray<ClassDeclarationSyntax> GenerateTypes(string source)
    {
        CSharpParseOptions parseOptions = new(LanguageVersion.CSharp12);
        ImmutableArray<MetadataReference> references = ReferenceAssemblies.Net.Net80
            .ResolveAsync(LanguageNames.CSharp, CancellationToken.None)
            .GetAwaiter()
            .GetResult()
            .AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "CoverageExclusionReceipts",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new MessagePack.SourceGenerator.MessagePackGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { generator.AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> generatorDiagnostics,
            CancellationToken.None);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        ImmutableArray<ClassDeclarationSyntax> generatedTypes = driver.GetRunResult().GeneratedTrees
            .SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            .ToImmutableArray();
        Assert.NotEmpty(generatedTypes);
        return generatedTypes;
    }

    private static void AssertAllDecorated(ImmutableArray<ClassDeclarationSyntax> generatedTypes)
    {
        foreach (ClassDeclarationSyntax generatedType in generatedTypes)
        {
            Assert.Contains(
                generatedType.AttributeLists.SelectMany(list => list.Attributes),
                attribute => attribute.Name.ToString().EndsWith("ExcludeFromCodeCoverage", StringComparison.Ordinal));
        }
    }
}
