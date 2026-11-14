// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterReferencedTypeTests
{
    private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12);

    [Fact]
    public async Task BodylessFormatterForClassInAssemblyWithoutMessagePack()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        MetadataReference projectA = EmitProjectA(
            """
            namespace ProjectA
            {
                public class MyObject
                {
                    public int Id { get; set; }
                    public string Name { get; set; }
                }
            }
            """,
            framework);

        string projectB = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }

                [GeneratedMessagePackFormatter]
                partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject>;
            }
            """;

        RunGeneratorAndAssertFormatter(projectB, WithMessagePack(framework, projectA), "ProjectB.MyObjectFormatter");
    }

    [Fact]
    public async Task FormatterForStructInAssemblyWithoutMessagePack()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        MetadataReference projectA = EmitProjectA(
            """
            namespace ProjectA
            {
                public struct Point
                {
                    public int X;
                    public int Y { get; set; }
                }
            }
            """,
            framework);

        string projectB = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }

                [GeneratedMessagePackFormatter]
                public partial class PointFormatter : IMessagePackFormatter<ProjectA.Point>
                {
                }
            }
            """;

        RunGeneratorAndAssertFormatter(projectB, WithMessagePack(framework, projectA), "ProjectB.PointFormatter");
    }

    private static MetadataReference[] WithMessagePack(ImmutableArray<MetadataReference> framework, params MetadataReference[] extra)
    {
        return framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).Concat(extra).ToArray();
    }

    private static MetadataReference EmitProjectA(string source, IEnumerable<MetadataReference> references)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static string RunGeneratorAndAssertFormatter(string source, IEnumerable<MetadataReference> references, string formatterMetadataName)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePackGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        GeneratorDriverRunResult run = driver.GetRunResult();

        Assert.All(run.Results, r => Assert.Null(r.Exception));
        List<Diagnostic> errors = generatorDiagnostics.Concat(output.GetDiagnostics()).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));

        INamedTypeSymbol? formatter = output.GetTypeByMetadataName(formatterMetadataName);
        Assert.NotNull(formatter);
        Assert.NotEmpty(formatter!.GetMembers("Serialize"));
        Assert.NotEmpty(formatter.GetMembers("Deserialize"));

        return string.Join("\n", run.Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()));
    }
}
