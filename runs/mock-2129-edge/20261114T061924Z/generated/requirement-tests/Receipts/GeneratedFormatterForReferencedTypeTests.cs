// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterForReferencedTypeTests
{
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
    public async Task FormatterIsGeneratedForTypeInReferencedAssemblyWithoutMessagePack()
    {
        string projectBSource = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                public partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject>
                {
                }
            }
            """;

        (string generated, ImmutableArray<Diagnostic> errors) = await RunAsync(projectBSource);
        Assert.Empty(errors);
        Assert.Contains("partial class MyObjectFormatter", generated);
        Assert.Contains("Label", generated);
    }

    [Fact]
    public async Task BodylessFormatterDeclarationCompiles()
    {
        string projectBSource = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject>;
            }
            """;

        (string generated, ImmutableArray<Diagnostic> errors) = await RunAsync(projectBSource);
        Assert.Empty(errors);
        Assert.Contains("MyObjectFormatter", generated);
    }

    private static async Task<(string Generated, ImmutableArray<Diagnostic> Errors)> RunAsync(string projectBSource)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest);

        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(ProjectASource, parseOptions) },
            framework,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream peStream = new MemoryStream();
        var emitResult = projectA.Emit(peStream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference projectAReference = MetadataReference.CreateFromImage(peStream.ToArray().ToImmutableArray());

        List<MetadataReference> references = new List<MetadataReference>(framework);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack).Cast<MetadataReference>());
        references.Add(projectAReference);

        CSharpCompilation projectB = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(projectBSource, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new MessagePackGenerator().AsSourceGenerator() }, parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(projectB, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);

        string generated = string.Join(Environment.NewLine, driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()));
        ImmutableArray<Diagnostic> errors = generatorDiagnostics
            .Concat(output.GetDiagnostics())
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
        return (generated, errors);
    }
}
