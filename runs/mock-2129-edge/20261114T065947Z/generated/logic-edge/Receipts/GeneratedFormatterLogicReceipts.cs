// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterLogicReceipts
{
    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Latest);

    [Fact]
    public async Task DataTypeIgnoreMemberAttributeIsHonoured()
    {
        List<MetadataReference> references = await GetReferencesAsync();
        MetadataReference projectA = EmitProjectA(
            """
            using MessagePack;

            namespace ProjectA
            {
                [MessagePackObject]
                public class MyType
                {
                    [Key(0)]
                    public int Value { get; set; }

                    [IgnoreMember]
                    public string SecretIgnoredMember { get; set; }
                }
            }
            """,
            references.ToArray());
        references.Add(projectA);

        string projectB = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                public partial class MyTypeFormatter : IMessagePackFormatter<ProjectA.MyType>
                {
                }

                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }
            }
            """;

        (GeneratorDriverRunResult result, _) = RunGenerator(projectB, references.ToArray());
        string? formatterSource = result.GeneratedTrees
            .Select(t => t.GetText().ToString())
            .FirstOrDefault(t => t.Contains("class MyTypeFormatter", StringComparison.Ordinal));

        Assert.NotNull(formatterSource);
        Assert.DoesNotContain("SecretIgnoredMember", formatterSource!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FormatterImplementingTwoFormatterInterfacesIsReported()
    {
        List<MetadataReference> references = await GetReferencesAsync();
        MetadataReference projectA = EmitProjectA(
            """
            namespace ProjectA
            {
                public class First
                {
                    public int A { get; set; }
                }

                public class Second
                {
                    public int B { get; set; }
                }
            }
            """,
            references.ToArray());
        references.Add(projectA);

        string projectB = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                public partial class TwoFormatter : IMessagePackFormatter<ProjectA.First>, IMessagePackFormatter<ProjectA.Second>
                {
                }

                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }
            }
            """;

        (GeneratorDriverRunResult result, _) = RunGenerator(projectB, references.ToArray());
        Assert.Contains(result.Diagnostics, d => d.Id == "MsgPack020");
    }

    [Fact]
    public async Task NestedFormatterInNonPartialOuterIsDiagnosed()
    {
        List<MetadataReference> references = await GetReferencesAsync();
        MetadataReference projectA = EmitProjectA(
            """
            namespace ProjectA
            {
                public class Plain
                {
                    public int Value { get; set; }
                }
            }
            """,
            references.ToArray());
        references.Add(projectA);

        string projectB = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                public class Outer
                {
                    [GeneratedMessagePackFormatter]
                    public partial class PlainFormatter : IMessagePackFormatter<ProjectA.Plain>
                    {
                    }
                }

                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }
            }
            """;

        (GeneratorDriverRunResult result, Compilation output) = RunGenerator(projectB, references.ToArray());
        bool reportedMsgPack = result.Diagnostics.Any(d => d.Id.StartsWith("MsgPack", StringComparison.Ordinal));
        bool hasMissingPartial = output.GetDiagnostics().Any(d => d.Id == "CS0260");
        Assert.True(reportedMsgPack || !hasMissingPartial, "Generated code requires Outer to be partial but no MsgPack diagnostic was reported.");
    }

    private static async Task<List<MetadataReference>> GetReferencesAsync()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> list = new List<MetadataReference>(framework);
        list.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        return list;
    }

    private static MetadataReference EmitProjectA(string source, MetadataReference[] references)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitResult = compilation.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static (GeneratorDriverRunResult Result, Compilation Output) RunGenerator(string source, MetadataReference[] references)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
        return (driver.GetRunResult(), output);
    }
}
