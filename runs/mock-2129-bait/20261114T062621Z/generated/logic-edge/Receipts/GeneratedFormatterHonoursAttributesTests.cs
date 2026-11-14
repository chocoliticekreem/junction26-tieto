// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterHonoursAttributesTests
{
    private const string ProjectASource = """
        namespace ProjectA
        {
            [MessagePack.MessagePackObject]
            public class MyObject
            {
                [MessagePack.Key(0)]
                public int Value { get; set; }

                [MessagePack.IgnoreMember]
                public int Ignored { get; set; }
            }
        }
        """;

    private const string ProjectBSource = """
        using MessagePack;
        using MessagePack.Formatters;

        namespace ProjectB
        {
            [GeneratedMessagePackResolver]
            partial class MyResolver { }

            [GeneratedMessagePackFormatter]
            public partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject> { }
        }
        """;

    [Fact]
    public async Task IgnoreMemberOnFormattedTypeIsHonoured()
    {
        string formatterSource = await GenerateFormatterSourceAsync();
        formatterSource.Should().NotContain("Ignored", "[IgnoreMember] on the formatted type must be honoured");
    }

    [Fact]
    public async Task IntKeysOnFormattedTypeAreHonoured()
    {
        string formatterSource = await GenerateFormatterSourceAsync();
        formatterSource.Should().NotContain("WriteMapHeader", "[Key(0)] on the formatted type declares an int-keyed (array) contract");
    }

    private static async Task<string> GenerateFormatterSourceAsync()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest);

        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(ProjectASource, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        List<MetadataReference> referencesB = new(references) { projectA.ToMetadataReference() };
        CSharpCompilation projectB = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(ProjectBSource, parseOptions) },
            referencesB,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGenerators(projectB);
        GeneratorDriverRunResult result = driver.GetRunResult();

        string? formatterSource = result.GeneratedTrees
            .Select(t => t.GetText().ToString())
            .FirstOrDefault(text => text.Contains("partial class MyObjectFormatter"));
        formatterSource.Should().NotBeNull("the generator should emit the body of MyObjectFormatter");
        return formatterSource!;
    }
}
