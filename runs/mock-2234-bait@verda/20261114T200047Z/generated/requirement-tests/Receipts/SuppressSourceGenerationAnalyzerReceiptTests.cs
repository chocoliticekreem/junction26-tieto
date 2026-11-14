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
    public async Task SuppressedType_ReportsMissingKeys()
    {
        GeneratorDriverRunResult result = await RunGeneratorAsync("""
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class SuppressedType
            {
                public int Value { get; set; }
            }

            [GeneratedMessagePackResolver]
            public partial class TestResolver
            {
            }
            """);

        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Id == "MsgPack004");
    }

    [Fact]
    public async Task SuppressedType_ReportsDuplicateKeys()
    {
        GeneratorDriverRunResult result = await RunGeneratorAsync("""
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class SuppressedType
            {
                [Key(0)]
                public int First { get; set; }

                [Key(0)]
                public int Second { get; set; }
            }

            [GeneratedMessagePackResolver]
            public partial class TestResolver
            {
            }
            """);

        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Id == "MsgPack005");
    }

    [Fact]
    public async Task SuppressedType_ReportsUnserializableMemberType()
    {
        GeneratorDriverRunResult result = await RunGeneratorAsync("""
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class SuppressedType
            {
                [Key(0)]
                public UnannotatedType Value { get; set; }
            }

            public class UnannotatedType
            {
                public int Value { get; set; }
            }

            [GeneratedMessagePackResolver]
            public partial class TestResolver
            {
            }
            """);

        result.Diagnostics.Should().Contain(diagnostic => diagnostic.Id == "MsgPack003");
    }

    [Fact]
    public async Task SuppressedType_GeneratesNoFormatter()
    {
        GeneratorDriverRunResult result = await RunGeneratorAsync("""
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class SuppressedType
            {
                [Key(0)]
                public int Value { get; set; }
            }

            [GeneratedMessagePackResolver]
            public partial class TestResolver
            {
            }
            """);

        result.Diagnostics.Should().NotContain(diagnostic => diagnostic.Id.StartsWith("MsgPack", StringComparison.Ordinal));
        result.GeneratedSources.Should().Contain(source => source.HintName.EndsWith("TestResolver.g.cs", StringComparison.Ordinal));
        result.GeneratedSources.Should().NotContain(source => source.HintName.Contains("SuppressedType", StringComparison.Ordinal));
    }

    private static async Task<GeneratorDriverRunResult> RunGeneratorAsync(string source)
    {
        CSharpParseOptions parseOptions = new(LanguageVersion.CSharp7_3);
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        ImmutableArray<MetadataReference> references = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        references = references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "SuppressSourceGenerationReceipts",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }
}
