// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class SuppressSourceGenerationAnalyzerReceiptTests
{
    [Fact]
    public async Task DuplicateKeysStillProduceMsgPack005WhenSourceGenerationIsSuppressed()
    {
        const string source = """
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class MyObject
            {
                [Key(0)]
                public int A { get; set; }

                [Key(0)]
                public int B { get; set; }
            }
            """;

        GeneratorDriverRunResult result = await RunGeneratorAsync(source);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "MsgPack005" && diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "CS8785");
    }

    [Fact]
    public async Task MixedKeyKindsStillProduceMsgPack006WhenSourceGenerationIsSuppressed()
    {
        const string source = """
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class MyObject
            {
                [Key(0)]
                public int A { get; set; }

                [Key("one")]
                public string B { get; set; } = "";
            }
            """;

        GeneratorDriverRunResult result = await RunGeneratorAsync(source);

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Id == "MsgPack006" && diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "CS8785");
    }

    [Fact]
    public async Task SuppressedValidTypeProducesNoGeneratedSources()
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

        GeneratorDriverRunResult result = await RunGeneratorAsync(source);

        Assert.Empty(result.Results.SelectMany(static generatorResult => generatorResult.GeneratedSources));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "CS8785");
    }

    private static async Task<GeneratorDriverRunResult> RunGeneratorAsync(string source)
    {
        ImmutableArray<MetadataReference> references = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        references = references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: "Receipts.SuppressSourceGeneration.Input",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source) },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }
}
