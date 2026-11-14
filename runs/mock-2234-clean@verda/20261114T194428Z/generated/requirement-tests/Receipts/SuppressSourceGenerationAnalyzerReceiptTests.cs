// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class SuppressSourceGenerationAnalyzerReceiptTests
{
    [Fact]
    public async Task SuppressedTypeIsAnalyzedWithoutGeneratingSource()
    {
        GeneratorDriverRunResult validResult = await RunGeneratorAsync("""
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class SuppressedPoco
            {
                [Key(0)]
                public int Value { get; set; }
            }
            """);

        validResult.GeneratedTrees.Should().BeEmpty("SuppressSourceGeneration must continue to suppress formatter generation");

        GeneratorDriverRunResult invalidResult = await RunGeneratorAsync("""
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class SuppressedPoco
            {
                public int Value { get; set; }
            }
            """);

        invalidResult.Diagnostics.Should().Contain(diagnostic => diagnostic.Id == "MsgPack004");
    }

    [Fact]
    public async Task DuplicateKeysAreReportedForSuppressedType()
    {
        GeneratorDriverRunResult result = await RunGeneratorAsync("""
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class SuppressedPoco
            {
                [Key(0)]
                public int First { get; set; }

                [Key(0)]
                public int Second { get; set; }
            }
            """);

        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Id == "MsgPack005");
        result.GeneratedTrees.Should().BeEmpty();
    }

    [Fact]
    public async Task MixedKeyKindsAreReportedForSuppressedType()
    {
        GeneratorDriverRunResult result = await RunGeneratorAsync("""
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class SuppressedPoco
            {
                [Key(0)]
                public int First { get; set; }

                [Key("second")]
                public string Second { get; set; }
            }
            """);

        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Id == "MsgPack005");
        result.GeneratedTrees.Should().BeEmpty();
    }

    [Fact]
    public async Task InitInitializerWarningIsReportedForSuppressedType()
    {
        GeneratorDriverRunResult result = await RunGeneratorAsync("""
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class SuppressedPoco
            {
                [Key(0)]
                public int Value { get; init; } = 42;
            }
            """);

        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Id == "MsgPack017");
        result.GeneratedTrees.Should().BeEmpty();
    }

    private static async Task<GeneratorDriverRunResult> RunGeneratorAsync(string source)
    {
        ImmutableArray<MetadataReference> references = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        references = references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "SuppressSourceGenerationReceipt",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }
}
