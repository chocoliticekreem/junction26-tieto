// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class SuppressSourceGenerationAnalyzerTests
{
    [Fact]
    public async Task SuppressedTypeStillGetsMsgPack004()
    {
        const string source = """
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class MyObject
            {
                public int {|MsgPack004:Value|} { get; set; }
            }
            """;

        await RunAnalyzerTestAsync(source);
    }

    [Theory]
    [InlineData("")]
    [InlineData("(SuppressSourceGeneration = false)")]
    public async Task UnsetOrFalseStillGetsMsgPack004(string attributeArguments)
    {
        string source = $$"""
            using MessagePack;

            [MessagePackObject{{attributeArguments}}]
            public partial class MyObject
            {
                public int {|MsgPack004:Value|} { get; set; }
            }
            """;

        await RunAnalyzerTestAsync(source);
    }

    [Fact]
    public async Task SuppressedTypeDoesNotGetGeneratedFormatter()
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

        var compilation = await CreateCompilationAsync(source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        var formatterSources = driver.GetRunResult().Results
            .SelectMany(result => result.GeneratedSources)
            .Where(generated => generated.SourceText.ToString().Contains("MyObject", StringComparison.Ordinal))
            .Where(generated => generated.SourceText.ToString().Contains("IFormatter", StringComparison.Ordinal));
        formatterSources.Should().BeEmpty("a type that suppresses source generation must rely on runtime serialization rather than a generated formatter");
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

    private static async Task<CSharpCompilation> CreateCompilationAsync(string source)
    {
        var references = (await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None))
            .AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        return CSharpCompilation.Create(
            "SuppressSourceGenerationReceipts",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp7_3)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
