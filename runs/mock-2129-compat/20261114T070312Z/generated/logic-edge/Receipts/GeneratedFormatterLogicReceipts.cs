// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class TimestampWriterReceipts
{
    private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void EpochPlusOneTickRoundTrips()
    {
        DateTime value = UnixEpoch.AddTicks(1);
        byte[] bytes = global::MessagePack.MessagePackSerializer.Serialize(value);
        DateTime result = global::MessagePack.MessagePackSerializer.Deserialize<DateTime>(bytes);
        Assert.Equal(value.Ticks, result.ToUniversalTime().Ticks);
    }

    [Fact]
    public void Year2106BoundaryRoundTrips()
    {
        DateTime value = UnixEpoch.AddSeconds(4294967296d);
        byte[] bytes = global::MessagePack.MessagePackSerializer.Serialize(value);
        DateTime result = global::MessagePack.MessagePackSerializer.Deserialize<DateTime>(bytes);
        Assert.Equal(value.Ticks, result.ToUniversalTime().Ticks);
    }

    [Fact]
    public void WholeSecondUsesTimestamp32()
    {
        DateTime value = UnixEpoch.AddSeconds(1);
        byte[] bytes = global::MessagePack.MessagePackSerializer.Serialize(value);
        Assert.Equal(new byte[] { 0xd6, 0xff, 0x00, 0x00, 0x00, 0x01 }, bytes);
    }
}

public class GeneratedFormatterLogicReceipts
{
    private static async Task<List<MetadataReference>> GetReferencesAsync()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        return Enumerable.Concat<MetadataReference>(framework, ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
    }

    private static async Task<MetadataReference> CompileProjectAAsync(string source)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            await GetReferencesAsync(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        using MemoryStream ms = new();
        var emit = compilation.Emit(ms);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return MetadataReference.CreateFromImage(ms.ToArray());
    }

    private static async Task<(GeneratorDriverRunResult Run, Compilation Output)> RunGeneratorAsync(string source, params MetadataReference[] extra)
    {
        CSharpParseOptions parseOptions = new(LanguageVersion.Latest);
        List<MetadataReference> references = await GetReferencesAsync();
        references.AddRange(extra);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
        return (driver.GetRunResult(), output);
    }

    [Fact]
    public async Task AttributedReferencedTypeHonoursIntKeys()
    {
        string projectA = """
            using MessagePack;
            namespace ProjectA
            {
                [MessagePackObject]
                public class MyType
                {
                    [Key(0)] public int A { get; set; }
                    [Key(1)] public string? B { get; set; }
                    [IgnoreMember] public int C { get; set; }
                }
            }
            """;
        string projectB = """
            using MessagePack;
            using MessagePack.Formatters;
            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                public partial class MyTypeFormatter : IMessagePackFormatter<ProjectA.MyType> { }

                [GeneratedMessagePackResolver]
                internal partial class MyResolver { }
            }
            """;

        MetadataReference projectARef = await CompileProjectAAsync(projectA);
        var (run, _) = await RunGeneratorAsync(projectB, projectARef);

        string? formatterSource = run.GeneratedTrees
            .Select(t => t.GetText().ToString())
            .FirstOrDefault(t => t.Contains("partial class MyTypeFormatter"));
        Assert.NotNull(formatterSource);
        Assert.Contains("WriteArrayHeader(", formatterSource);
        Assert.DoesNotContain("WriteMapHeader(", formatterSource);
    }

    [Fact]
    public async Task NestedInNonPartialOuterIsDiagnosedOrCompiles()
    {
        string projectB = """
            using MessagePack;
            using MessagePack.Formatters;
            namespace ProjectB
            {
                public class Plain
                {
                    public int Value { get; set; }
                }

                public class Outer
                {
                    [GeneratedMessagePackFormatter]
                    internal partial class PlainFormatter : IMessagePackFormatter<Plain> { }
                }

                [GeneratedMessagePackResolver]
                internal partial class MyResolver { }
            }
            """;

        var (run, output) = await RunGeneratorAsync(projectB);

        bool generatorReportedError = run.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
        List<Diagnostic> outputErrors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(
            generatorReportedError || outputErrors.Count == 0,
            "Generator reported no diagnostic but generated code does not compile: " + string.Join(Environment.NewLine, outputErrors));
    }
}
