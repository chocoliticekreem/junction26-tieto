// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterReferencedTypeTests(ITestOutputHelper logger)
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

    [Fact]
    public async Task BodylessFormatterForReferencedTypeCompiles()
    {
        string projectB = """
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

        (Compilation output, List<string> generated) = await this.RunAsync(projectB);
        List<Diagnostic> errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        foreach (Diagnostic error in errors)
        {
            logger.WriteLine(error.ToString());
        }

        Assert.Empty(errors);
        Assert.Contains(generated, s => s.Contains("class MyTypeFormatter"));
    }

    [Fact]
    public async Task FormatterForReferencedTypeWithoutAttributesSerializesPublicMembers()
    {
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

        (Compilation output, List<string> generated) = await this.RunAsync(projectB);
        List<Diagnostic> errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        foreach (Diagnostic error in errors)
        {
            logger.WriteLine(error.ToString());
        }

        Assert.Empty(errors);
        string? formatter = generated.FirstOrDefault(s => s.Contains("class MyTypeFormatter"));
        Assert.NotNull(formatter);
        Assert.Contains("Id", formatter);
        Assert.Contains("Name", formatter);
    }

    private async Task<(Compilation Output, List<string> Generated)> RunAsync(string projectBSource)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(ProjectASource) },
            framework,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        var emitResult = projectA.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference projectARef = MetadataReference.CreateFromImage(stream.ToArray());

        CSharpParseOptions parseOptions = new(LanguageVersion.CSharp12);
        IEnumerable<MetadataReference> references = framework.Concat(new MetadataReference[]
        {
            MetadataReference.CreateFromFile(typeof(MessagePack.MessagePackObjectAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(MessagePack.MessagePackSerializer).Assembly.Location),
            projectARef,
        });
        CSharpCompilation projectB = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(projectBSource, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(projectB, out Compilation output, out _);
        List<string> generated = driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()).ToList();
        foreach (string text in generated)
        {
            logger.WriteLine(text);
        }

        return (output, generated);
    }
}
