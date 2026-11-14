// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterLogicReceipts
{
    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Latest);

    private readonly ITestOutputHelper logger;

    public GeneratedFormatterLogicReceipts(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    [Fact]
    public void DateTimeWithSubSecondAtEpochRoundTrips()
    {
        DateTime value = new DateTime(1970, 1, 1, 0, 0, 0, 500, DateTimeKind.Utc);
        byte[] bytes = MessagePack.MessagePackSerializer.Serialize(value);
        DateTime result = MessagePack.MessagePackSerializer.Deserialize<DateTime>(bytes);
        Assert.Equal(value.Ticks, result.ToUniversalTime().Ticks);
    }

    [Fact]
    public void WholeSecondDateTimeUsesTimestamp32()
    {
        byte[] bytes = MessagePack.MessagePackSerializer.Serialize(new DateTime(1970, 1, 1, 0, 0, 1, DateTimeKind.Utc));
        Assert.Equal(new byte[] { 0xd6, 0xff, 0x00, 0x00, 0x00, 0x01 }, bytes);
    }

    [Fact]
    public async Task MessagePackAttributesOnReferencedTypeAreHonoured()
    {
        List<MetadataReference> references = await GetReferencesAsync();
        CSharpCompilation projectA = CreateCompilation("ProjectA", """
            using MessagePack;

            namespace ProjectA
            {
                [MessagePackObject]
                public class MyType
                {
                    [Key(0)]
                    public int Value { get; set; }

                    [IgnoreMember]
                    public int Secret { get; set; }
                }
            }
            """, references);
        Assert.Empty(projectA.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        List<MetadataReference> referencesB = new(references) { projectA.ToMetadataReference() };
        CSharpCompilation projectB = CreateCompilation("ProjectB", """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                public partial class MyTypeFormatter : IMessagePackFormatter<ProjectA.MyType> { }

                [GeneratedMessagePackResolver]
                partial class MyResolver { }
            }
            """, referencesB);

        (Compilation _, GeneratorDriverRunResult run) = RunGenerator(projectB);
        string generated = string.Join("\n", run.GeneratedTrees.Select(t => t.ToString()));
        this.logger.WriteLine(generated);
        Assert.Contains("MyTypeFormatter", generated);
        Assert.DoesNotContain("Secret", generated);
    }

    [Fact]
    public async Task ExplicitCallbackReceiverCompiles()
    {
        List<MetadataReference> references = await GetReferencesAsync();
        CSharpCompilation project = CreateCompilation("ProjectB", """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                public class Data : IMessagePackSerializationCallbackReceiver
                {
                    public int Value { get; set; }

                    void IMessagePackSerializationCallbackReceiver.OnBeforeSerialize() { }

                    void IMessagePackSerializationCallbackReceiver.OnAfterDeserialize() { }
                }

                [GeneratedMessagePackFormatter]
                public partial class DataFormatter : IMessagePackFormatter<Data> { }

                [GeneratedMessagePackResolver]
                partial class MyResolver { }
            }
            """, references);
        Assert.Empty(project.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        (Compilation output, GeneratorDriverRunResult run) = RunGenerator(project);
        foreach (SyntaxTree tree in run.GeneratedTrees)
        {
            this.logger.WriteLine(tree.ToString());
        }

        List<Diagnostic> errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        foreach (Diagnostic error in errors)
        {
            this.logger.WriteLine(error.ToString());
        }

        Assert.Empty(errors);
    }

    private static async Task<List<MetadataReference>> GetReferencesAsync()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> result = new(framework);
        result.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        return result;
    }

    private static CSharpCompilation CreateCompilation(string assemblyName, string source, IEnumerable<MetadataReference> references)
    {
        return CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static (Compilation Output, GeneratorDriverRunResult Run) RunGenerator(CSharpCompilation compilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
        return (output, driver.GetRunResult());
    }
}
