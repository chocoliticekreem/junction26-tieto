// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterReferencedAssemblyTests
{
    private static async Task<List<MetadataReference>> GetBaseReferencesAsync()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        return framework.Concat<MetadataReference>(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
    }

    private static (ImmutableArray<Diagnostic> GeneratorDiagnostics, Compilation Output) RunGenerator(string source, IEnumerable<MetadataReference> references)
    {
        CSharpParseOptions parseOptions = new(LanguageVersion.CSharp12);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
        return (diagnostics, output);
    }

    [Fact]
    public async Task FormatterIsGeneratedForTypeFromReferencedAssembly()
    {
        List<MetadataReference> baseReferences = await GetBaseReferencesAsync();

        string projectASource = """
            namespace ProjectA
            {
                public class MyType
                {
                    public int Id { get; set; }
                    public string Name { get; set; } = "";
                }
            }
            """;
        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(projectASource) },
            baseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        var emitResult = projectA.Emit(stream);
        emitResult.Success.Should().BeTrue();
        MetadataReference projectARef = MetadataReference.CreateFromImage(stream.ToArray());

        string projectBSource = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                public partial class MyTypeFormatter : IMessagePackFormatter<ProjectA.MyType> { }

                [GeneratedMessagePackResolver]
                partial class MyResolver { }
            }
            """;

        List<MetadataReference> projectBReferences = new(baseReferences) { projectARef };
        var (generatorDiagnostics, output) = RunGenerator(projectBSource, projectBReferences);

        generatorDiagnostics.Where(d => d.Id == "MsgPack021").Should().BeEmpty();
        output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }

    [Fact]
    public async Task FormatterHonoursIgnoreMemberOnFormattedType()
    {
        List<MetadataReference> baseReferences = await GetBaseReferencesAsync();

        string source = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [MessagePackObject]
                public class MyType
                {
                    [Key(0)]
                    public int Id { get; set; }

                    [IgnoreMember]
                    public int IgnoredSecret { get; set; }
                }

                [GeneratedMessagePackFormatter]
                public partial class MyTypeFormatter : IMessagePackFormatter<MyType> { }

                [GeneratedMessagePackResolver]
                partial class MyResolver { }
            }
            """;

        var (_, output) = RunGenerator(source, baseReferences);

        string? formatterSource = output.SyntaxTrees
            .Where(t => !string.IsNullOrEmpty(t.FilePath))
            .Select(t => t.GetText().ToString())
            .FirstOrDefault(text => text.Contains("partial class MyTypeFormatter"));

        formatterSource.Should().NotBeNull();
        formatterSource.Should().NotContain("IgnoredSecret");
    }
}
