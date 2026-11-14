// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterForReferencedTypeTests
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    private const string ProjectBSource = """
        using MessagePack;
        using MessagePack.Formatters;

        namespace ProjectB
        {
            [GeneratedMessagePackFormatter]
            public partial class MyTypeFormatter : IMessagePackFormatter<ProjectA.MyType>;

            [GeneratedMessagePackResolver]
            partial class MyResolver
            {
            }
        }
        """;

    [Fact]
    public async Task BodylessFormatter_ForTypeInAssemblyWithoutMessagePack_IsGenerated()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        string projectASource = """
            namespace ProjectA
            {
                public class MyType
                {
                    public int Id { get; set; }
                    public string Label { get; set; }
                }
            }
            """;
        MetadataReference projectA = EmitLibrary("ProjectA", projectASource, framework);

        (Compilation output, GeneratorDriverRunResult run) = RunGenerator(ProjectBSource, framework, projectA);

        Assert.Empty(run.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        string formatterSource = GetFormatterSource(run, "MyTypeFormatter");
        Assert.Contains("Id", formatterSource, StringComparison.Ordinal);
        Assert.Contains("Label", formatterSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrivateMembersOnReferencedType_DoNotBlockGeneration()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        string projectASource = """
            namespace ProjectA
            {
                public class MyType
                {
                    private int secretCounter;

                    public int Id { get; set; }

                    public int GetCounter() => this.secretCounter;
                }
            }
            """;
        MetadataReference projectA = EmitLibrary("ProjectA", projectASource, framework);

        (Compilation output, GeneratorDriverRunResult run) = RunGenerator(ProjectBSource, framework, projectA);

        Assert.Empty(run.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        string formatterSource = GetFormatterSource(run, "MyTypeFormatter");
        Assert.Contains("Id", formatterSource, StringComparison.Ordinal);
    }

    private static MetadataReference EmitLibrary(string name, string source, IEnumerable<MetadataReference> references)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            name,
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray().ToImmutableArray());
    }

    private static (Compilation Output, GeneratorDriverRunResult Run) RunGenerator(string source, ImmutableArray<MetadataReference> framework, MetadataReference projectA)
    {
        List<MetadataReference> references = framework
            .Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack).Cast<MetadataReference>())
            .Append(projectA)
            .ToList();
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new ISourceGenerator[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
        return (output, driver.GetRunResult());
    }

    private static string GetFormatterSource(GeneratorDriverRunResult run, string formatterName)
    {
        SyntaxTree? tree = run.GeneratedTrees.FirstOrDefault(t => t.ToString().Contains("partial class " + formatterName, StringComparison.Ordinal));
        Assert.True(tree is not null, "No generated source declares partial class " + formatterName);
        return tree!.ToString();
    }
}
