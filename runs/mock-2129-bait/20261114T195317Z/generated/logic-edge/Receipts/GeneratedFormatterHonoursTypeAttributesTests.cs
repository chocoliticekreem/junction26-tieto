// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterHonoursTypeAttributesTests
{
    private readonly ITestOutputHelper logger;

    public GeneratedFormatterHonoursTypeAttributesTests(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    [Fact]
    public async Task IgnoreMemberOnReferencedTypeIsHonoured()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = new(framework);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpParseOptions parseOptions = new(LanguageVersion.Latest);

        string librarySource = """
            using MessagePack;

            namespace Lib
            {
                [MessagePackObject]
                public class Data
                {
                    [Key(0)]
                    public int Value { get; set; }

                    [IgnoreMember]
                    public string SecretToken { get; set; } = "";
                }
            }
            """;

        CSharpCompilation library = CSharpCompilation.Create(
            "ReceiptsLib",
            new[] { CSharpSyntaxTree.ParseText(librarySource, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using MemoryStream stream = new();
        var emitResult = library.Emit(stream);
        foreach (Diagnostic d in emitResult.Diagnostics)
        {
            this.logger.WriteLine(d.ToString());
        }

        emitResult.Success.Should().BeTrue();
        MetadataReference libraryReference = MetadataReference.CreateFromImage(stream.ToArray());

        string appSource = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace App
            {
                [GeneratedMessagePackFormatter]
                public partial class DataFormatter : IMessagePackFormatter<Lib.Data> { }
            }
            """;

        List<MetadataReference> appReferences = new(references) { libraryReference };
        CSharpCompilation app = CSharpCompilation.Create(
            "ReceiptsApp",
            new[] { CSharpSyntaxTree.ParseText(appSource, parseOptions) },
            appReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(app, out Compilation _, out ImmutableArray<Diagnostic> _);

        GeneratorDriverRunResult runResult = driver.GetRunResult();
        List<string> generated = runResult.GeneratedTrees.Select(t => t.ToString()).ToList();
        foreach (string text in generated)
        {
            this.logger.WriteLine(text);
        }

        string? formatterSource = generated.FirstOrDefault(t => t.Contains("partial class DataFormatter"));
        formatterSource.Should().NotBeNull("the generator should write the body of the [GeneratedMessagePackFormatter] class");
        formatterSource!.Should().NotContain("SecretToken", "[IgnoreMember] on the formatted type must be honoured");
    }
}
