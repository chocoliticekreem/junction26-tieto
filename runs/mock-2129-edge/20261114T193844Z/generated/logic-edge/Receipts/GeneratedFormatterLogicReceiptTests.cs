// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterLogicReceiptTests
{
    private static async Task<List<MetadataReference>> GetBaseReferencesAsync()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> refs = new(framework);
        refs.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        return refs;
    }

    private static GeneratorDriverRunResult RunGenerator(string source, IEnumerable<MetadataReference> references)
    {
        CSharpParseOptions parseOptions = new(LanguageVersion.CSharp12);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGenerators(compilation);
        return driver.GetRunResult();
    }

    [Fact]
    public async Task MessagePackAttributesOnReferencedType_AreHonoured()
    {
        List<MetadataReference> refs = await GetBaseReferencesAsync();
        string projectA = """
            using MessagePack;
            namespace ProjectA
            {
                [MessagePackObject]
                public class MyType
                {
                    [Key(0)]
                    public int Id { get; set; }

                    [IgnoreMember]
                    public string? IgnoredSecretValue { get; set; }
                }
            }
            """;
        CSharpCompilation compA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(projectA) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        using MemoryStream ms = new();
        var emit = compA.Emit(ms);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        refs.Add(MetadataReference.CreateFromImage(ms.ToArray()));

        string projectB = """
            using MessagePack;
            using MessagePack.Formatters;
            namespace ProjectB
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver { }

                [GeneratedMessagePackFormatter]
                public partial class MyTypeFormatter : IMessagePackFormatter<ProjectA.MyType> { }
            }
            """;
        GeneratorDriverRunResult result = RunGenerator(projectB, refs);
        string allGenerated = string.Join("\n", result.Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()));

        Assert.Contains("MyTypeFormatter", allGenerated);
        Assert.DoesNotContain("IgnoredSecretValue", allGenerated);
    }

    [Fact]
    public async Task FormatterImplementingTwoDataTypes_ReportsMsgPack020()
    {
        List<MetadataReference> refs = await GetBaseReferencesAsync();
        string source = """
            using MessagePack;
            using MessagePack.Formatters;
            namespace ProjectB
            {
                public class DataA { public int X { get; set; } }
                public class DataB { public int Y { get; set; } }

                [GeneratedMessagePackResolver]
                partial class MyResolver { }

                [GeneratedMessagePackFormatter]
                public partial class TwoFormatter : IMessagePackFormatter<DataA>, IMessagePackFormatter<DataB> { }
            }
            """;
        GeneratorDriverRunResult result = RunGenerator(source, refs);

        Assert.Contains(result.Diagnostics, d => d.Id == "MsgPack020");
    }
}
