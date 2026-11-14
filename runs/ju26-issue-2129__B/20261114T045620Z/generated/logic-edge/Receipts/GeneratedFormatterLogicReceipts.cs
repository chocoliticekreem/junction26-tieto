// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterLogicReceipts
{
    private static async Task<(GeneratorDriverRunResult Run, Compilation Output, ImmutableArray<Diagnostic> GeneratorDiagnostics)> RunGeneratorAsync(string source)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = new(framework)
        {
            MetadataReference.CreateFromFile(typeof(global::MessagePack.MessagePackSerializer).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(global::MessagePack.KeyAttribute).Assembly.Location),
        };

        CSharpParseOptions parseOptions = new(LanguageVersion.Latest);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        return (driver.GetRunResult(), output, generatorDiagnostics);
    }

    [Theory]
    [InlineData("IgnoreMember")]
    [InlineData("IgnoreDataMember")]
    public async Task IgnoredMemberOnFormattedTypeIsNotSerialized(string ignoreAttribute)
    {
        string source = $$"""
            using System.Runtime.Serialization;
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB;

            public class MyObject
            {
                public int Visible { get; set; }

                [{{ignoreAttribute}}]
                public int Secret { get; set; }
            }

            [GeneratedMessagePackFormatter]
            public partial class MyObjectFormatter : IMessagePackFormatter<MyObject> { }

            [GeneratedMessagePackResolver]
            partial class MyResolver { }
            """;

        var (run, _, _) = await RunGeneratorAsync(source);
        string? formatterSource = run.GeneratedTrees
            .Select(t => t.ToString())
            .FirstOrDefault(t => t.Contains("partial class MyObjectFormatter"));

        Assert.NotNull(formatterSource);
        Assert.Contains("Visible", formatterSource);
        Assert.DoesNotContain("Secret", formatterSource);
    }

    [Fact]
    public async Task FormatterNestedInNonPartialTypeIsDiagnosedOrCompiles()
    {
        string source = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB;

            public class MyObject
            {
                public int Value { get; set; }
            }

            public class Outer
            {
                [GeneratedMessagePackFormatter]
                public partial class MyObjectFormatter : IMessagePackFormatter<MyObject> { }
            }

            [GeneratedMessagePackResolver]
            partial class MyResolver { }
            """;

        var (_, output, generatorDiagnostics) = await RunGeneratorAsync(source);
        bool reportedPartialDiagnostic = generatorDiagnostics.Any(d => d.Id == "MsgPack019");
        bool hasMissingPartialError = output.GetDiagnostics().Any(d => d.Id == "CS0260");

        Assert.True(
            reportedPartialDiagnostic || !hasMissingPartialError,
            "A formatter nested in a non-partial type must either be diagnosed (MsgPack019) or generate compilable code, but generated code produced CS0260 with no MessagePack diagnostic.");
    }
}
