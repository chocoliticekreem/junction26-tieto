// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterReferencedAttributesTests
{
    [Fact]
    public async Task MessagePackObjectKeysOnReferencedTypeAreHonored()
    {
        ImmutableArray<MetadataReference> frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        ImmutableArray<MetadataReference> messagePackReferences = frameworkReferences.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        CSharpParseOptions parseOptions = new(LanguageVersion.Latest);

        CSharpCompilation referencedCompilation = CSharpCompilation.Create(
            "ProjectA",
            new[]
            {
                CSharpSyntaxTree.ParseText(
                    """
                    using MessagePack;

                    [MessagePackObject]
                    public class AttributedType
                    {
                        [Key(0)]
                        public int Value { get; set; }
                    }
                    """,
                    parseOptions),
            },
            messagePackReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using MemoryStream referencedImage = new();
        referencedCompilation.Emit(referencedImage).Success.Should().BeTrue();
        MetadataReference referencedAssembly = MetadataReference.CreateFromImage(referencedImage.ToArray());

        CSharpCompilation consumingCompilation = CSharpCompilation.Create(
            "ProjectB",
            new[]
            {
                CSharpSyntaxTree.ParseText(
                    """
                    using MessagePack;
                    using MessagePack.Formatters;

                    [GeneratedMessagePackFormatter]
                    public partial class AttributedTypeFormatter : IMessagePackFormatter<AttributedType>
                    {
                    }
                    """,
                    parseOptions),
            },
            messagePackReferences.Add(referencedAssembly),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(consumingCompilation, out _, out _, CancellationToken.None);

        string[] formatterSources = driver.GetRunResult().Results
            .SelectMany(result => result.GeneratedSources)
            .Select(source => source.SourceText.ToString())
            .Where(source => source.Contains("partial class AttributedTypeFormatter", StringComparison.Ordinal))
            .ToArray();

        formatterSources.Should().ContainSingle();
        string formatterSource = formatterSources.Single();
        formatterSource.Should().Contain("WriteArrayHeader");
        formatterSource.Should().NotContain("WriteMapHeader");
    }
}
