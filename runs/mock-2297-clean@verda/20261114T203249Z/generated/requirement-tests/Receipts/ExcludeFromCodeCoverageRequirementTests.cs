// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageRequirementTests
{
    private const string ObjectAndResolverSource = """
        using MessagePack;

        [MessagePackObject]
        public class Sample
        {
            [Key(0)]
            public int Value { get; set; }
        }

        [GeneratedMessagePackResolver]
        internal partial class SampleResolver
        {
        }
        """;

    [Fact]
    public async Task GeneratedFormatterAndResolverTypesAreDecorated()
    {
        (GeneratorDriverRunResult result, Compilation output) = await GenerateAsync(ObjectAndResolverSource);

        AssertCompiles(output);
        AssertAllGeneratedClassesAreDecorated(result);
    }

    [Fact]
    public async Task GeneratedStringKeyAndEnumFormatterTypesAreDecorated()
    {
        const string source = """
            using MessagePack;

            public enum SampleEnum
            {
                Unknown,
                Value,
            }

            [MessagePackObject]
            public class EnumContainer
            {
                [Key(0)]
                public SampleEnum Value { get; set; }
            }

            [MessagePackObject(true)]
            public class StringKeySample
            {
                public int Value { get; set; }
            }
            """;

        (GeneratorDriverRunResult result, Compilation output) = await GenerateAsync(source);

        AssertCompiles(output);
        AssertAllGeneratedClassesAreDecorated(result);
    }

    [Fact]
    public async Task GeneratedCodeCompilesWhenTheAttributeIsExcludedByTargetFramework()
    {
        (GeneratorDriverRunResult result, Compilation output) = await GenerateAsync(ObjectAndResolverSource);
        AssertCompiles(output);
        AssertAllGeneratedClassesAreDecorated(result);

        foreach (string targetFrameworkSymbol in new[] { "NETSTANDARD2_0", "NETFRAMEWORK" })
        {
            (_, Compilation conditionalOutput) = await GenerateAsync(ObjectAndResolverSource, targetFrameworkSymbol);
            AssertCompiles(conditionalOutput);
        }
    }

    private static async Task<(GeneratorDriverRunResult Result, Compilation Output)> GenerateAsync(string source, params string[] preprocessorSymbols)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3, preprocessorSymbols: preprocessorSymbols);
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);

        var references = new List<MetadataReference>();
        references.AddRange(await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None));
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ExcludeFromCodeCoverageRequirementTests",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out _);

        return (driver.GetRunResult(), outputCompilation);
    }

    private static void AssertCompiles(Compilation compilation)
    {
        compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should()
            .BeEmpty("generated code must remain compilable");
    }

    private static void AssertAllGeneratedClassesAreDecorated(GeneratorDriverRunResult result)
    {
        ClassDeclarationSyntax[] generatedClasses = result.GeneratedTrees
            .SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            .ToArray();

        generatedClasses.Should().NotBeEmpty("the test inputs require generated formatter or resolver code");
        generatedClasses.Should().OnlyContain(
            type => type.AttributeLists
                .SelectMany(list => list.Attributes)
                .Any(attribute => attribute.Name.ToString().EndsWith("ExcludeFromCodeCoverage", StringComparison.Ordinal)),
            "every generated class should be excluded from code coverage");
    }
}
