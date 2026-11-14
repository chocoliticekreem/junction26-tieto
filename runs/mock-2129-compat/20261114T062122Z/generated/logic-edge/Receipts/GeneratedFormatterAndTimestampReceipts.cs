// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterAndTimestampReceipts
{
    private const string AttributedProjectASource = """
        namespace ProjectA
        {
            [MessagePack.MessagePackObject]
            public class MyObject
            {
                [MessagePack.Key(0)]
                public int Value { get; set; }

                [MessagePack.IgnoreMember]
                public int Secret { get; set; }
            }
        }
        """;

    [Fact]
    public void DateTime_EpochPlusOneTick_RoundTrips()
    {
        DateTime value = DateTime.SpecifyKind(new DateTime(1970, 1, 1, 0, 0, 0).AddTicks(1), DateTimeKind.Utc);
        byte[] bytes = global::MessagePack.MessagePackSerializer.Serialize(value);
        DateTime result = global::MessagePack.MessagePackSerializer.Deserialize<DateTime>(bytes);
        Assert.Equal(value.Ticks, result.ToUniversalTime().Ticks);
    }

    [Fact]
    public void DateTime_WholeSecond_UsesTimestamp32()
    {
        DateTime value = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        byte[] bytes = global::MessagePack.MessagePackSerializer.Serialize(value);
        Assert.Equal(6, bytes.Length);
    }

    [Fact]
    public async Task AttributedReferencedType_IgnoreMemberIsHonoured()
    {
        string formatter = await GenerateFormatterSourceAsync(AttributedProjectASource);
        Assert.DoesNotContain("Secret", formatter);
    }

    [Fact]
    public async Task AttributedReferencedType_IntKeysProduceArray()
    {
        string formatter = await GenerateFormatterSourceAsync(AttributedProjectASource);
        Assert.Contains("WriteArrayHeader", formatter);
    }

    private static async Task<string> GenerateFormatterSourceAsync(string projectASource)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = new(framework);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpParseOptions parseOptions = new(LanguageVersion.Latest);
        CSharpCompilationOptions compilationOptions = new(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable);

        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(projectASource, parseOptions) },
            references,
            compilationOptions);
        using MemoryStream stream = new();
        var emitResult = projectA.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        references.Add(MetadataReference.CreateFromImage(stream.ToArray().ToImmutableArray()));

        const string projectBSource = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver { }

                [GeneratedMessagePackFormatter]
                partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject> { }
            }
            """;

        CSharpCompilation projectB = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(projectBSource, parseOptions) },
            references,
            compilationOptions);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGenerators(projectB);
        GeneratorDriverRunResult result = driver.GetRunResult();

        string? formatter = result.GeneratedTrees
            .Select(t => t.ToString())
            .FirstOrDefault(t => t.Contains("class MyObjectFormatter"));
        Assert.NotNull(formatter);
        return formatter!;
    }
}
