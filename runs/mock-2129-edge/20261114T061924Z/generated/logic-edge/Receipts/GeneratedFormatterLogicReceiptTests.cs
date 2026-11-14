// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterLogicReceiptTests
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    private static async Task<List<MetadataReference>> GetBaseReferencesAsync()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> refs = new(framework);
        foreach (MetadataReference r in ReferencesHelper.GetReferences(ReferencesSet.MessagePack))
        {
            refs.Add(r);
        }

        return refs;
    }

    private static MetadataReference BuildProjectA(string source, List<MetadataReference> refs)
    {
        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream ms = new();
        var emitResult = projectA.Emit(ms);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        return MetadataReference.CreateFromImage(ms.ToArray());
    }

    private static GeneratorDriverRunResult RunGenerator(string projectBSource, List<MetadataReference> refs)
    {
        CSharpCompilation projectB = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(projectBSource, ParseOptions) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGenerators(projectB);
        GeneratorDriverRunResult result = driver.GetRunResult();
        foreach (GeneratorRunResult r in result.Results)
        {
            Assert.Null(r.Exception);
        }

        return result;
    }

    [Fact]
    public async Task AttributesOnFormattedTypeAreHonoured()
    {
        List<MetadataReference> refs = await GetBaseReferencesAsync();
        string projectASource = """
            using MessagePack;

            namespace ProjectA
            {
                [MessagePackObject]
                public class MyObject
                {
                    [Key(0)]
                    public int Value { get; set; }

                    [IgnoreMember]
                    public int Secret { get; set; }
                }
            }
            """;
        refs.Add(BuildProjectA(projectASource, refs));

        string projectBSource = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject> { }

                [GeneratedMessagePackResolver]
                partial class MyResolver { }
            }
            """;

        GeneratorDriverRunResult result = RunGenerator(projectBSource, refs);
        string? formatterSource = result.GeneratedTrees
            .Select(t => t.GetText().ToString())
            .FirstOrDefault(s => s.Contains("partial class MyObjectFormatter"));

        Assert.NotNull(formatterSource);
        Assert.DoesNotContain(".Secret", formatterSource);
    }

    [Fact]
    public async Task TwoFormatterInterfacesReportMsgPack020()
    {
        List<MetadataReference> refs = await GetBaseReferencesAsync();
        string projectASource = """
            namespace ProjectA
            {
                public class A
                {
                    public int X { get; set; }
                }

                public class B
                {
                    public int Y { get; set; }
                }
            }
            """;
        refs.Add(BuildProjectA(projectASource, refs));

        string projectBSource = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                partial class TwoFormatter : IMessagePackFormatter<ProjectA.A>, IMessagePackFormatter<ProjectA.B> { }

                [GeneratedMessagePackResolver]
                partial class MyResolver { }
            }
            """;

        GeneratorDriverRunResult result = RunGenerator(projectBSource, refs);
        Assert.Contains(result.Diagnostics, d => d.Id == "MsgPack020");
    }
}
