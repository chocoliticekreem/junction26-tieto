// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterForReferencedTypeTests(ITestOutputHelper logger)
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp12);

    private const string ProjectASource = """
        namespace ProjectA
        {
            public class MyObject
            {
                public int Id { get; set; }
                public string Label { get; set; }
            }
        }
        """;

    [Fact]
    public async Task FormatterWithBodyGeneratedForTypeFromAssemblyWithoutMessagePack()
    {
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

        (string generated, ImmutableArray<Diagnostic> errors) = await RunAsync(ProjectASource, projectB);
        logger.WriteLine(generated);
        Assert.True(errors.IsEmpty, string.Join(Environment.NewLine, errors));
        Assert.Contains("MyObjectFormatter", generated);
        Assert.Contains("Id", generated);
        Assert.Contains("Label", generated);
    }

    [Fact]
    public async Task BodylessFormatterDeclarationCompiles()
    {
        string projectB = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB;

            [GeneratedMessagePackFormatter]
            partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject>;

            [GeneratedMessagePackResolver]
            partial class MyResolver
            {
            }
            """;

        (string generated, ImmutableArray<Diagnostic> errors) = await RunAsync(ProjectASource, projectB);
        logger.WriteLine(generated);
        Assert.True(errors.IsEmpty, string.Join(Environment.NewLine, errors));
        Assert.Contains("MyObjectFormatter", generated);
    }

    private static async Task<(string Generated, ImmutableArray<Diagnostic> Errors)> RunAsync(string projectASource, string projectBSource)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(projectASource, ParseOptions) },
            framework,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        var emitResult = projectA.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference projectARef = MetadataReference.CreateFromImage(stream.ToArray());

        CSharpCompilation projectB = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(projectBSource, ParseOptions) },
            framework.Add(projectARef),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddReferences(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(projectB, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        string generated = string.Join("\n", driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()));
        ImmutableArray<Diagnostic> errors = generatorDiagnostics.Concat(output.GetDiagnostics())
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
        return (generated, errors);
    }
}
