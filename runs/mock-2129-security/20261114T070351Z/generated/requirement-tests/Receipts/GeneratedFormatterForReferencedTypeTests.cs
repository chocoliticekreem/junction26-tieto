// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterForReferencedTypeTests
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp12);

    private const string ProjectASource = """
        namespace ProjectA
        {
            public class MyType
            {
                public int OrderNumber { get; set; }

                public string CustomerName { get; set; }
            }
        }
        """;

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
    public async Task BodylessFormatterForReferencedType_IsImplementedByGenerator()
    {
        (Compilation output, ImmutableArray<Diagnostic> generatorDiagnostics, string generated) = await BuildAsync();

        Assert.DoesNotContain(generatorDiagnostics, d => d.Severity == DiagnosticSeverity.Error || d.Id == "CS8785");
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("MyTypeFormatter", generated);
    }

    [Fact]
    public async Task FormatterForTypeWithoutAttributes_SerializesPublicMembers()
    {
        (Compilation output, ImmutableArray<Diagnostic> generatorDiagnostics, string generated) = await BuildAsync();

        Assert.DoesNotContain(generatorDiagnostics, d => d.Severity == DiagnosticSeverity.Error || d.Id == "CS8785");
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("OrderNumber", generated);
        Assert.Contains("CustomerName", generated);
    }

    private static async Task<(Compilation Output, ImmutableArray<Diagnostic> GeneratorDiagnostics, string Generated)> BuildAsync()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);

        // Project A references only the framework, never MessagePack.
        MetadataReference projectA = Emit("ProjectA", ProjectASource, framework);

        List<MetadataReference> projectBReferences = new(framework);
        projectBReferences.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        projectBReferences.Add(projectA);

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(ProjectBSource, ParseOptions) },
            projectBReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
        GeneratorDriverRunResult run = driver.GetRunResult();
        string generated = string.Join("\n", run.GeneratedTrees.Select(t => t.ToString()));
        return (output, diagnostics, generated);
    }

    private static MetadataReference Emit(string assemblyName, string source, IEnumerable<MetadataReference> references)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }
}
