// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterLogicReceiptsTests
{
    private readonly ITestOutputHelper logger;

    public GeneratedFormatterLogicReceiptsTests(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    [Fact]
    public async Task IgnoreMemberOnReferencedTypeIsHonoured()
    {
        List<MetadataReference> references = await GetReferencesAsync();
        CSharpParseOptions parseOptions = new(LanguageVersion.Latest);

        string projectASource = """
            using MessagePack;
            namespace ProjectA
            {
                [MessagePackObject]
                public class MyType
                {
                    [Key("v")]
                    public int Value { get; set; }

                    [IgnoreMember]
                    public string Secret { get; set; }
                }
            }
            """;
        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(projectASource, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        string projectBSource = """
            using MessagePack;
            using MessagePack.Formatters;
            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                public partial class MyTypeFormatter : IMessagePackFormatter<ProjectA.MyType> { }

                [GeneratedMessagePackResolver]
                partial class MyResolver { }
            }
            """;
        List<MetadataReference> projectBReferences = new(references) { projectA.ToMetadataReference() };

        string generated = RunGenerator(projectBSource, projectBReferences, parseOptions, out _);
        this.logger.WriteLine(generated);

        Assert.Contains("MyTypeFormatter", generated);
        Assert.DoesNotContain("Secret", generated);
    }

    [Fact]
    public async Task ExplicitCallbackReceiverCompiles()
    {
        List<MetadataReference> references = await GetReferencesAsync();
        CSharpParseOptions parseOptions = new(LanguageVersion.Latest);

        string source = """
            using MessagePack;
            using MessagePack.Formatters;
            namespace ProjectB
            {
                public class MyType : IMessagePackSerializationCallbackReceiver
                {
                    public int Value { get; set; }

                    void IMessagePackSerializationCallbackReceiver.OnBeforeSerialize() { }

                    void IMessagePackSerializationCallbackReceiver.OnAfterDeserialize() { }
                }

                [GeneratedMessagePackFormatter]
                public partial class MyTypeFormatter : IMessagePackFormatter<MyType> { }

                [GeneratedMessagePackResolver]
                partial class MyResolver { }
            }
            """;

        string generated = RunGenerator(source, references, parseOptions, out Compilation output);
        this.logger.WriteLine(generated);

        Assert.Contains("MyTypeFormatter", generated);
        List<Diagnostic> errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        foreach (Diagnostic error in errors)
        {
            this.logger.WriteLine(error.ToString());
        }

        Assert.Empty(errors);
    }

    private static string RunGenerator(string source, List<MetadataReference> references, CSharpParseOptions parseOptions, out Compilation output)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out output, out ImmutableArray<Diagnostic> _);

        GeneratorDriverRunResult result = driver.GetRunResult();
        return string.Join("\n// ----\n", result.GeneratedTrees.Select(t => t.ToString()));
    }

    private static async Task<List<MetadataReference>> GetReferencesAsync()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = new(framework);
        foreach (MetadataReference reference in ReferencesHelper.GetReferences(ReferencesSet.MessagePack))
        {
            references.Add(reference);
        }

        return references;
    }
}
