// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using MsgPackGenerator = MessagePack.SourceGenerator.MessagePackGenerator;

namespace Receipts.GeneratedTests;

public class GeneratedMessagePackFormatterRequirementsTests
{
    [Fact]
    public async Task BodylessFormatterForAttributeFreeReferencedTypeIsGenerated()
    {
        const string projectASource = """
            namespace ProjectA;

            public class MyObject
            {
                public int Id { get; set; }

                public string? Name { get; set; }
            }
            """;
        MetadataReference projectA = await CompileReferenceAsync("ProjectA", projectASource, includeMessagePack: false);

        const string projectBSource = """
            using MessagePack;
            using MessagePack.Formatters;

            [GeneratedMessagePackFormatter]
            public partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject>;
            """;
        var (outputCompilation, generatorDiagnostics, generatedSource) = await RunGeneratorAsync(projectBSource, projectA);

        generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        generatedSource.Should().Contain("partial class MyObjectFormatter");
        generatedSource.Should().Contain("Serialize(");
        generatedSource.Should().Contain("Deserialize(");
    }

    [Fact]
    public async Task MessagePackAttributesOnReferencedTypeAreHonored()
    {
        const string projectASource = """
            using MessagePack;

            namespace ProjectA;

            [MessagePackObject]
            public class MyObject
            {
                [Key(7)]
                public int Id { get; set; }

                [IgnoreMember]
                public string? Secret { get; set; }
            }
            """;
        MetadataReference projectA = await CompileReferenceAsync("ProjectA", projectASource, includeMessagePack: true);

        const string projectBSource = """
            using MessagePack;
            using MessagePack.Formatters;

            [GeneratedMessagePackFormatter]
            public partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject>
            {
            }
            """;
        var (outputCompilation, generatorDiagnostics, generatedSource) = await RunGeneratorAsync(projectBSource, projectA);

        generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        generatedSource.Should().Contain("Write(7)");
        generatedSource.Should().NotContain("Secret");
    }

    private static async Task<MetadataReference> CompileReferenceAsync(string assemblyName, string source, bool includeMessagePack)
    {
        ImmutableArray<MetadataReference> frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        IEnumerable<MetadataReference> references = frameworkReferences;
        if (includeMessagePack)
        {
            references = references.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        }

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static async Task<(Compilation OutputCompilation, ImmutableArray<Diagnostic> GeneratorDiagnostics, string GeneratedSource)> RunGeneratorAsync(
        string source,
        MetadataReference projectA)
    {
        ImmutableArray<MetadataReference> frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        IEnumerable<MetadataReference> references = frameworkReferences
            .Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack))
            .Append(projectA);

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MsgPackGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out ImmutableArray<Diagnostic> diagnostics);
        string generatedSource = string.Join("\n", driver.GetRunResult().GeneratedTrees.Select(tree => tree.GetText().ToString()));
        return (outputCompilation, diagnostics, generatedSource);
    }
}
