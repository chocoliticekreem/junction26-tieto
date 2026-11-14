// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using MessagePack;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class Req2129ReceiptTests
{
    [Fact]
    public async Task GeneratedFormatterHonoursMessagePackAttributes()
    {
        const string source = """
            using MessagePack;
            using MessagePack.Formatters;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class AttributedPoco
            {
                [Key(0)]
                public int Value { get; set; }

                [IgnoreMember]
                public int Ignored { get; set; }
            }

            [GeneratedMessagePackFormatter]
            public partial class AttributedPocoGeneratedFormatter : IMessagePackFormatter<AttributedPoco>;
            """;

        ImmutableArray<MetadataReference> frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        ImmutableArray<MetadataReference> references = frameworkReferences.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "GeneratedFormatterAttributeReceipt",
            new SyntaxTree[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out _);

        outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();

        string generated = string.Join(
            Environment.NewLine,
            driver.GetRunResult().Results
                .SelectMany(result => result.GeneratedSources)
                .Select(generatedSource => generatedSource.SourceText.ToString()));

        generated.Should().Contain("AttributedPocoGeneratedFormatter");
        generated.Should().NotContain("Ignored", "[IgnoreMember] on the formatted type must be honoured");
    }

    [Fact]
    public void TimestampAtEpochWithSubsecondsRoundTrips()
    {
        DateTime value = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(500);

        byte[] bytes = MessagePackSerializer.Serialize(value);
        DateTime roundTripped = MessagePackSerializer.Deserialize<DateTime>(bytes);

        roundTripped.Should().Be(value);
    }
}
