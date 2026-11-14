// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterReferencedTypeTests
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp12);

    [Fact]
    public async Task BodylessFormatterForReferencedClassWithoutAttributesCompiles()
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
        string projectB = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                partial class MyObjectFormatter : IMessagePackFormatter<global::ProjectA.MyObject>;

                [GeneratedMessagePackResolver]
                partial class MyResolver { }
            }
            """;

        MetadataReference projectARef = await BuildProjectAAsync(projectA);
        (Compilation output, GeneratorDriverRunResult run) = await RunProjectBAsync(projectB, projectARef);

        AssertNoErrors(output, run);
        string formatterSource = string.Join("\n", run.GeneratedTrees.Select(t => t.ToString()).Where(t => t.Contains("class MyObjectFormatter")));
        Assert.False(string.IsNullOrEmpty(formatterSource), "No generated source declares MyObjectFormatter.");
        Assert.Contains("Serialize(", formatterSource);
        Assert.Contains("Deserialize(", formatterSource);
        Assert.Contains("Id", formatterSource);
        Assert.Contains("Label", formatterSource);
    }

    [Fact]
    public async Task BodylessFormatterForReferencedStructWithoutAttributesCompiles()
    {
        string projectA = """
            namespace ProjectA
            {
                public struct Point
                {
                    public int X;
                    public int Y;
                }
            }
            """;
        string projectB = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                partial class PointFormatter : IMessagePackFormatter<global::ProjectA.Point>;

                [GeneratedMessagePackResolver]
                partial class MyResolver { }
            }
            """;

        MetadataReference projectARef = await BuildProjectAAsync(projectA);
        (Compilation output, GeneratorDriverRunResult run) = await RunProjectBAsync(projectB, projectARef);

        AssertNoErrors(output, run);
        string formatterSource = string.Join("\n", run.GeneratedTrees.Select(t => t.ToString()).Where(t => t.Contains("class PointFormatter")));
        Assert.False(string.IsNullOrEmpty(formatterSource), "No generated source declares PointFormatter.");
        Assert.Contains("Serialize(", formatterSource);
        Assert.Contains("Deserialize(", formatterSource);
    }

    private static void AssertNoErrors(Compilation output, GeneratorDriverRunResult run)
    {
        Diagnostic[] generatorErrors = run.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(generatorErrors.Length == 0, string.Join(Environment.NewLine, generatorErrors.Select(d => d.ToString())));
        Diagnostic[] compileErrors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(compileErrors.Length == 0, string.Join(Environment.NewLine, compileErrors.Select(d => d.ToString())));
    }

    private static async Task<List<MetadataReference>> GetReferencesAsync(bool includeMessagePack)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = new(framework);
        if (includeMessagePack)
        {
            references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        }

        return references;
    }

    private static async Task<MetadataReference> BuildProjectAAsync(string source)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            await GetReferencesAsync(includeMessagePack: false),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.ToString())));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static async Task<(Compilation Output, GeneratorDriverRunResult Run)> RunProjectBAsync(string source, MetadataReference projectA)
    {
        List<MetadataReference> references = await GetReferencesAsync(includeMessagePack: true);
        references.Add(projectA);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
        return (output, driver.GetRunResult());
    }
}
