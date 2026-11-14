// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterReferencedAssemblyTests
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp12);

    private static async Task<List<MetadataReference>> GetBaseReferencesAsync(bool includeMessagePack)
    {
        List<MetadataReference> references = new();
        references.AddRange(await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None));
        if (includeMessagePack)
        {
            references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        }

        return references;
    }

    private static async Task<MetadataReference> CompileProjectAAsync(string source)
    {
        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            await GetBaseReferencesAsync(includeMessagePack: false),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        var emitResult = projectA.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static async Task<(Compilation Output, ImmutableArray<Diagnostic> GeneratorDiagnostics, GeneratorDriverRunResult RunResult)> RunProjectBAsync(string source, MetadataReference projectA)
    {
        List<MetadataReference> references = await GetBaseReferencesAsync(includeMessagePack: true);
        references.Add(projectA);
        CSharpCompilation projectB = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(projectB, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
        return (output, diagnostics, driver.GetRunResult());
    }

    private static void AssertNoErrors(Compilation output, ImmutableArray<Diagnostic> generatorDiagnostics)
    {
        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public async Task BodylessFormatterForClassInAssemblyWithoutMessagePackCompiles()
    {
        MetadataReference projectA = await CompileProjectAAsync("""
            namespace ProjectA
            {
                public class MyObject
                {
                    public int Id { get; set; }
                    public string Name { get; set; } = "";
                }
            }
            """);

        var (output, generatorDiagnostics, runResult) = await RunProjectBAsync("""
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject>;

                [GeneratedMessagePackResolver]
                partial class MyResolver { }
            }
            """, projectA);

        AssertNoErrors(output, generatorDiagnostics);
        Assert.Contains(runResult.GeneratedTrees, t => t.ToString().Contains("MyObjectFormatter"));
    }

    [Fact]
    public async Task FormatterForStructInAssemblyWithoutMessagePackCompiles()
    {
        MetadataReference projectA = await CompileProjectAAsync("""
            namespace ProjectA
            {
                public struct MyPoint
                {
                    public int X;
                    public int Y { get; set; }
                }
            }
            """);

        var (output, generatorDiagnostics, runResult) = await RunProjectBAsync("""
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                public partial class MyPointFormatter : IMessagePackFormatter<ProjectA.MyPoint>
                {
                }

                [GeneratedMessagePackResolver]
                partial class MyResolver { }
            }
            """, projectA);

        AssertNoErrors(output, generatorDiagnostics);
        Assert.Contains(runResult.GeneratedTrees, t => t.ToString().Contains("MyPointFormatter"));
    }
}
