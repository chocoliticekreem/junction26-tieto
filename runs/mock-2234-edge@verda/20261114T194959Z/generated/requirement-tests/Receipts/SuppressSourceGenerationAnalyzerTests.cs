// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class SuppressSourceGenerationAnalyzerTests
{
    [Fact]
    public async Task AnalyzerReportsMissingKeyWhenSourceGenerationIsSuppressed()
    {
        const string source = """
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public partial class MyObject
            {
                public int {|MsgPack004:Value|} { get; set; }
            }
            """;

        await RunAnalyzerTestAsync(source);
    }

    [Fact]
    public async Task AnalyzerReportsMissingKeyWhenSourceGenerationIsNotSuppressed()
    {
        const string source = """
            using MessagePack;

            [MessagePackObject]
            public partial class MyObject
            {
                public int {|MsgPack004:Value|} { get; set; }
            }
            """;

        await RunAnalyzerTestAsync(source);
    }

    [Fact]
    public async Task DuplicateKeysDoNotCrashAnalysisWhenSourceGenerationIsSuppressed()
    {
        const string source = """
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public partial class MyObject
            {
                [Key(0)]
                public int First { get; set; }

                [Key(0)]
                public int Second { get; set; }
            }
            """;

        Compilation compilation = await CreateCompilationAsync(source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out ImmutableArray<Diagnostic> diagnostics);

        diagnostics.Should().NotContain(
            diagnostic => diagnostic.Id == "CS8785",
            "a generator failure prevents the suppressed type from being analysed");
    }

    [Fact]
    public async Task SuppressedTypeDoesNotProduceGeneratedSources()
    {
        const string source = """
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class MyObject
            {
                [Key(0)]
                public int Value { get; set; }
            }
            """;

        Compilation compilation = await CreateCompilationAsync(source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out ImmutableArray<Diagnostic> diagnostics);

        diagnostics.Should().NotContain(diagnostic => diagnostic.Id == "CS8785");
        GeneratorRunResult result = driver.GetRunResult().Results.Single();
        result.GeneratedSources.Should().BeEmpty("source generation was explicitly suppressed");
    }

    private static async Task RunAnalyzerTestAsync(string source)
    {
        var test = new VerifyCS.Test
        {
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
        };
        test.TestState.Sources.Add(source);
        await test.RunAsync();
    }

    private static async Task<Compilation> CreateCompilationAsync(string source)
    {
        ImmutableArray<MetadataReference> frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        ImmutableArray<MetadataReference> references = frameworkReferences.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        return CSharpCompilation.Create(
            "SuppressSourceGenerationReceipts",
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
