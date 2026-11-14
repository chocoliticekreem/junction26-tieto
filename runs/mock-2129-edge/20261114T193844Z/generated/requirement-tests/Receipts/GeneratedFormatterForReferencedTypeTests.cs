// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterForReferencedTypeTests
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp12);

    private const string ProjectASource = """
        namespace ProjectA
        {
            public class MyType
            {
                private int hidden = 0;
                public int Id { get; set; }
                public string Name { get; set; } = "";
                public int GetHidden() => this.hidden;
            }
        }
        """;

    private const string ProjectBSource = """
        using MessagePack;
        using MessagePack.Formatters;

        namespace ProjectB;

        [GeneratedMessagePackFormatter]
        partial class MyTypeFormatter : IMessagePackFormatter<ProjectA.MyType>;

        [GeneratedMessagePackResolver]
        partial class MyResolver
        {
        }
        """;

    [Fact]
    public async Task BodylessFormatterForReferencedTypeIsImplemented()
    {
        MetadataReference projectA = await CompileProjectAAsync(ProjectASource);
        (Compilation output, ImmutableArray<Diagnostic> generatorDiagnostics, string[] generated) = await RunGeneratorAsync(projectA, ProjectBSource);

        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Diagnostic[] errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(e => e.ToString())));
        Assert.Contains(generated, s => s.Contains("partial class MyTypeFormatter"));
    }

    [Fact]
    public async Task ContractlessFormatterCoversPublicMembersOnly()
    {
        MetadataReference projectA = await CompileProjectAAsync(ProjectASource);
        (Compilation _, ImmutableArray<Diagnostic> _, string[] generated) = await RunGeneratorAsync(projectA, ProjectBSource);

        string formatterSource = string.Join("\n", generated.Where(s => s.Contains("partial class MyTypeFormatter")));
        Assert.False(string.IsNullOrEmpty(formatterSource), "No formatter body was generated for MyTypeFormatter.");
        Assert.Contains("Id", formatterSource);
        Assert.Contains("Name", formatterSource);
        Assert.DoesNotContain("hidden", formatterSource);
    }

    private static async Task<MetadataReference> CompileProjectAAsync(string source)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            framework,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.ToString())));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static async Task<(Compilation Output, ImmutableArray<Diagnostic> GeneratorDiagnostics, string[] Generated)> RunGeneratorAsync(MetadataReference projectA, string source)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = Enumerable.Concat<MetadataReference>(framework, ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        references.Add(projectA);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
        string[] generated = driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()).ToArray();
        return (output, diagnostics, generated);
    }
}
