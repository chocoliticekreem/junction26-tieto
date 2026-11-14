// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterLogicReceipts
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp12);

    private readonly ITestOutputHelper logger;

    public GeneratedFormatterLogicReceipts(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    [Fact]
    public async Task FormatterBodyIsGeneratedWithoutDeclaredResolver()
    {
        MetadataReference projectA = await CompileProjectAAsync(
            "namespace ProjectA { public class MyType { public int Value { get; set; } public string Name { get; set; } } }",
            referenceMessagePack: false);

        string projectB = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                public partial class MyTypeFormatter : IMessagePackFormatter<ProjectA.MyType>;
            }
            """;

        (Compilation output, ImmutableArray<Diagnostic> generatorDiagnostics, GeneratorDriverRunResult run) = await this.RunProjectBAsync(projectB, projectA);

        Assert.DoesNotContain(generatorDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        List<Diagnostic> errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors.Select(e => e.ToString())));
    }

    [Fact]
    public async Task AttributedReferencedTypeHonoursIgnoreMember()
    {
        MetadataReference projectA = await CompileProjectAAsync(
            """
            using MessagePack;

            namespace ProjectA
            {
                [MessagePackObject]
                public class Tagged
                {
                    [Key(0)]
                    public int Value { get; set; }

                    [IgnoreMember]
                    public int Secret { get; set; }
                }
            }
            """,
            referenceMessagePack: true);

        string projectB = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                public partial class TaggedFormatter : IMessagePackFormatter<ProjectA.Tagged>;

                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }
            }
            """;

        (Compilation output, ImmutableArray<Diagnostic> generatorDiagnostics, GeneratorDriverRunResult run) = await this.RunProjectBAsync(projectB, projectA);

        Assert.DoesNotContain(generatorDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(run.GeneratedTrees, t => t.ToString().Contains("TaggedFormatter"));
        Assert.DoesNotContain(run.GeneratedTrees, t => t.ToString().Contains("Secret"));
    }

    private static async Task<MetadataReference> CompileProjectAAsync(string source, bool referenceMessagePack)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = new(framework);
        if (referenceMessagePack)
        {
            references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        }

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using MemoryStream stream = new();
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.ToString())));
        return MetadataReference.CreateFromImage(stream.ToArray().ToImmutableArray());
    }

    private async Task<(Compilation Output, ImmutableArray<Diagnostic> GeneratorDiagnostics, GeneratorDriverRunResult Run)> RunProjectBAsync(string source, MetadataReference projectA)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = new(framework);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        references.Add(projectA);

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
        GeneratorDriverRunResult run = driver.GetRunResult();

        foreach (SyntaxTree tree in run.GeneratedTrees)
        {
            this.logger.WriteLine("// " + tree.FilePath);
            this.logger.WriteLine(tree.ToString());
        }

        foreach (Diagnostic diagnostic in diagnostics)
        {
            this.logger.WriteLine(diagnostic.ToString());
        }

        return (output, diagnostics, run);
    }
}
