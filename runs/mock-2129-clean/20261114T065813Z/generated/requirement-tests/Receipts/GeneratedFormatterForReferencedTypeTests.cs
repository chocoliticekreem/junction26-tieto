// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
                public string Name { get; set; }
            }
        }
        """;

    private static async Task<(ImmutableArray<MetadataReference> Framework, ImmutableArray<MetadataReference> All)> GetReferencesAsync()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        ImmutableArray<MetadataReference> all = framework.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        return (framework, all);
    }

    private static MetadataReference BuildProjectA(IEnumerable<MetadataReference> references)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(ProjectASource) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        var emitResult = compilation.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static async Task<(Compilation Output, ImmutableArray<Diagnostic> GeneratorDiagnostics, GeneratorDriverRunResult RunResult)> RunProjectBAsync(string source)
    {
        var (framework, all) = await GetReferencesAsync();
        MetadataReference projectA = BuildProjectA(framework);
        CSharpParseOptions parseOptions = new(LanguageVersion.CSharp12);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            all.Add(projectA),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
        return (output, diagnostics, driver.GetRunResult());
    }

    private static void AssertNoErrors(Compilation output, ImmutableArray<Diagnostic> generatorDiagnostics)
    {
        List<Diagnostic> errors = generatorDiagnostics.Concat(output.GetDiagnostics()).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public async Task BodylessFormatterForReferencedType_IsImplemented()
    {
        string source = """
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
        var (output, diagnostics, runResult) = await RunProjectBAsync(source);
        AssertNoErrors(output, diagnostics);
        Assert.Contains(runResult.GeneratedTrees, t => t.ToString().Contains("MyObjectFormatter"));
    }

    [Fact]
    public async Task FormatterWithEmptyBodyForReferencedType_IsImplemented()
    {
        string source = """
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
        var (output, diagnostics, runResult) = await RunProjectBAsync(source);
        AssertNoErrors(output, diagnostics);
        Assert.Contains(runResult.GeneratedTrees, t => t.ToString().Contains("MyObjectFormatter"));
    }
}
