// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterForReferencedTypeTests
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp12);

    [Fact]
    public async Task BodylessFormatter_ForReferencedTypeWithoutAttributes_IsGenerated()
    {
        string projectA = """
            namespace ProjectA
            {
                public class MyObject
                {
                    public int Id { get; set; }
                    public string Label { get; set; }
                    private int hiddenCounter = 0;
                    public int GetHidden() => this.hiddenCounter;
                }
            }
            """;
        string projectB = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject>;

                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }
            }
            """;

        var result = await RunAsync(projectA, projectB);

        Assert.True(result.Errors.IsEmpty, string.Join(Environment.NewLine, result.Errors));
        Assert.Contains("partial class MyObjectFormatter", result.GeneratedText);
        Assert.Contains("Label", result.GeneratedText);
        Assert.DoesNotContain("hiddenCounter", result.GeneratedText);
    }

    [Fact]
    public async Task FormatterForReferencedStruct_IsGenerated()
    {
        string projectA = """
            namespace ProjectA
            {
                public struct Point
                {
                    public int XCoordinate { get; set; }
                    public int YCoordinate { get; set; }
                }
            }
            """;
        string projectB = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                public partial class PointFormatter : IMessagePackFormatter<ProjectA.Point>
                {
                }

                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }
            }
            """;

        var result = await RunAsync(projectA, projectB);

        Assert.True(result.Errors.IsEmpty, string.Join(Environment.NewLine, result.Errors));
        Assert.Contains("partial class PointFormatter", result.GeneratedText);
        Assert.Contains("XCoordinate", result.GeneratedText);
        Assert.Contains("YCoordinate", result.GeneratedText);
    }

    private static async Task<(ImmutableArray<Diagnostic> Errors, string GeneratedText)> RunAsync(string projectASource, string projectBSource)
    {
        ImmutableArray<MetadataReference> framework = await Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);

        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(projectASource, ParseOptions) },
            framework,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream image = new();
        var emitResult = projectA.Emit(image);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference projectAReference = MetadataReference.CreateFromImage(image.ToArray());

        List<MetadataReference> references = new(framework);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        references.Add(projectAReference);

        CSharpCompilation projectB = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(projectBSource, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(projectB, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);

        GeneratorDriverRunResult run = driver.GetRunResult();
        string text = string.Join(Environment.NewLine, run.GeneratedTrees.Select(t => t.ToString()));
        ImmutableArray<Diagnostic> errors = generatorDiagnostics
            .Concat(output.GetDiagnostics())
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
        return (errors, text);
    }
}
