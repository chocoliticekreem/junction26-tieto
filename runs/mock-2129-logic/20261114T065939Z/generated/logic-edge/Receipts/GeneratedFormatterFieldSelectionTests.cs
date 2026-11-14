// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterFieldSelectionTests
{
    private static async Task<(Compilation Output, string GeneratedText)> RunGeneratorAsync(string source)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = new(framework);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpParseOptions parseOptions = new(LanguageVersion.Latest);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> _);
        GeneratorDriverRunResult result = driver.GetRunResult();
        string text = string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));
        return (output, text);
    }

    [Fact]
    public async Task MutablePublicFieldIsSerialized()
    {
        string source = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectA
            {
                public class MyObject
                {
                    public int PropMarker { get; set; }
                    public int MutableFieldMarker;
                }
            }

            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                public partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject> { }

                [GeneratedMessagePackResolver]
                partial class MyResolver { }
            }
            """;

        (Compilation _, string generated) = await RunGeneratorAsync(source);
        generated.Should().Contain("PropMarker", "the read-write property must be serialized in contractless mode");
        generated.Should().Contain("MutableFieldMarker", "a public read-write field must be serialized in contractless mode");
    }

    [Fact]
    public async Task ReadonlyPublicFieldIsNotAssigned()
    {
        string source = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectA
            {
                public class MyObject
                {
                    public int PropMarker { get; set; }
                    public readonly int ReadonlyFieldMarker;
                }
            }

            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                public partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject> { }

                [GeneratedMessagePackResolver]
                partial class MyResolver { }
            }
            """;

        (Compilation output, string generated) = await RunGeneratorAsync(source);
        output.GetDiagnostics().Where(d => d.Id == "CS0191").Should().BeEmpty("generated code must not assign a readonly field");
        generated.Should().NotContain("ReadonlyFieldMarker", "a readonly field is not read-write and must not be part of the contractless contract");
    }
}
