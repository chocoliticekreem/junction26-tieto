// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterForReferencedAssemblyTypeTests(ITestOutputHelper logger)
{
    private const string ProjectASource = """
        namespace ProjectA
        {
            public class MyType
            {
                public int Id { get; set; }

                public string Name { get; set; }
            }
        }
        """;

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp12);

    [Fact]
    public async Task FormatterIsGeneratedForTypeFromMetadataReference()
    {
        string projectBSource = """
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
        await this.AssertFormatterGeneratedAsync(projectBSource);
    }

    [Fact]
    public async Task BodylessFormatterIsGeneratedForTypeFromMetadataReference()
    {
        string projectBSource = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                partial class MyTypeFormatter : IMessagePackFormatter<ProjectA.MyType>;

                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }
            }
            """;
        await this.AssertFormatterGeneratedAsync(projectBSource);
    }

    private async Task AssertFormatterGeneratedAsync(string projectBSource)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        CSharpCompilationOptions compilationOptions = new(OutputKind.DynamicallyLinkedLibrary);

        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(ProjectASource, ParseOptions) },
            framework,
            compilationOptions);
        Assert.DoesNotContain(projectA.ReferencedAssemblyNames, a => a.Name.StartsWith("MessagePack", StringComparison.Ordinal));

        using MemoryStream peStream = new();
        Microsoft.CodeAnalysis.Emit.EmitResult emitResult = projectA.Emit(peStream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        List<MetadataReference> references = new(framework);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        references.Add(MetadataReference.CreateFromImage(peStream.ToArray().ToImmutableArray()));

        CSharpCompilation projectB = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(projectBSource, ParseOptions) },
            references,
            compilationOptions);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(projectB, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        GeneratorDriverRunResult runResult = driver.GetRunResult();

        foreach (SyntaxTree tree in runResult.GeneratedTrees)
        {
            logger.WriteLine(tree.FilePath);
            logger.WriteLine(tree.ToString());
        }

        foreach (Diagnostic diagnostic in generatorDiagnostics)
        {
            logger.WriteLine(diagnostic.ToString());
        }

        List<Diagnostic> outputErrors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        foreach (Diagnostic diagnostic in outputErrors)
        {
            logger.WriteLine(diagnostic.ToString());
        }

        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(outputErrors);
        Assert.Contains(runResult.GeneratedTrees, t =>
        {
            string text = t.ToString();
            return text.Contains("MyTypeFormatter") && text.Contains("Deserialize") && text.Contains(".Name");
        });
    }
}
