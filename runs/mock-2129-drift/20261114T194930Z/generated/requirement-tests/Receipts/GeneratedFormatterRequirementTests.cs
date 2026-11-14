// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterRequirementTests
{
    private static readonly CSharpParseOptions TestParseOptions = new(LanguageVersion.CSharp12);

    private static async Task<List<MetadataReference>> GetFrameworkReferencesAsync()
    {
        ImmutableArray<MetadataReference> refs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        return refs.ToList();
    }

    private static (Compilation Output, GeneratorDriverRunResult Result) RunGenerator(string source, IEnumerable<MetadataReference> references)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(source, TestParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: TestParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
        return (output, driver.GetRunResult());
    }

    private static List<string> GeneratedTexts(GeneratorDriverRunResult result)
    {
        return result.Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()).ToList();
    }

    private static void AssertNoErrors(Compilation output, GeneratorDriverRunResult result)
    {
        Assert.All(result.Results, r => Assert.Null(r.Exception));
        List<Diagnostic> generatorErrors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(generatorErrors.Count == 0, string.Join(Environment.NewLine, generatorErrors));
        List<Diagnostic> compileErrors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(compileErrors.Count == 0, string.Join(Environment.NewLine, compileErrors));
    }

    [Fact]
    public async Task FormatterIsGeneratedForTypeFromReferencedAssembly()
    {
        string projectA = """
            namespace ProjectA
            {
                public class MyObject
                {
                    public int Id { get; set; }
                    public string Label { get; set; }
                }
            }
            """;
        List<MetadataReference> framework = await GetFrameworkReferencesAsync();
        CSharpCompilation compilationA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(projectA, TestParseOptions) },
            framework,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        var emitResult = compilationA.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference projectAReference = MetadataReference.CreateFromImage(stream.ToArray());

        List<MetadataReference> references = new(framework);
        references.Add(projectAReference);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        string projectB = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                public partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject>
                {
                }

                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }
            }
            """;
        (Compilation output, GeneratorDriverRunResult result) = RunGenerator(projectB, references);

        AssertNoErrors(output, result);
        Assert.Contains(GeneratedTexts(result), t => t.Contains("MyObjectFormatter") && t.Contains("Label"));
    }

    [Fact]
    public async Task BodylessFormatterDeclarationIsGenerated()
    {
        List<MetadataReference> references = await GetFrameworkReferencesAsync();
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        string source = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                public class MyObject
                {
                    public int Id { get; set; }
                    public string Label { get; set; }
                }

                [GeneratedMessagePackFormatter]
                public partial class MyObjectFormatter : IMessagePackFormatter<MyObject>;

                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }
            }
            """;
        (Compilation output, GeneratorDriverRunResult result) = RunGenerator(source, references);

        AssertNoErrors(output, result);
        Assert.Contains(GeneratedTexts(result), t => t.Contains("MyObjectFormatter") && t.Contains("Label") && t.Contains("Id"));
    }

    [Fact]
    public async Task MessagePackAttributesOnDataTypeAreHonoured()
    {
        List<MetadataReference> references = await GetFrameworkReferencesAsync();
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        string source = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [MessagePackObject]
                public class MyObject
                {
                    [Key(0)]
                    public int Id { get; set; }

                    [IgnoreMember]
                    public string Secret { get; set; }
                }

                [GeneratedMessagePackFormatter]
                public partial class CustomMyObjectFormatter : IMessagePackFormatter<MyObject>
                {
                }

                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }
            }
            """;
        (Compilation output, GeneratorDriverRunResult result) = RunGenerator(source, references);

        Assert.All(result.Results, r => Assert.Null(r.Exception));
        List<string> formatterSources = GeneratedTexts(result).Where(t => t.Contains("CustomMyObjectFormatter")).ToList();
        Assert.NotEmpty(formatterSources);
        Assert.All(formatterSources, t => Assert.DoesNotContain("Secret", t));
    }
}
