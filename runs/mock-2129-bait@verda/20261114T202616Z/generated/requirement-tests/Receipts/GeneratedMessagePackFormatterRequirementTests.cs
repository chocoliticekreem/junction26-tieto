// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public sealed class GeneratedMessagePackFormatterRequirementTests
{
    [Fact]
    public async Task GeneratesBodylessFormatterForAttributeFreeTypeInReferencedAssembly()
    {
        const string projectA = """
            namespace ProjectA
            {
                public class MyObject
                {
                    public int Id { get; set; }
                    public string? Name { get; set; }
                }
            }
            """;

        const string projectB = """
            using MessagePack;
            using MessagePack.Formatters;

            [GeneratedMessagePackFormatter]
            public partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject>;

            [GeneratedMessagePackResolver]
            public partial class TestResolver
            {
            }
            """;

        var result = await GenerateAsync(projectA, projectB, includeMessagePackInProjectA: false);

        result.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        result.CompilerDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        result.Sources.Should().Contain("partial class MyObjectFormatter");
        result.Sources.Should().Contain("Id");
        result.Sources.Should().Contain("Name");
    }

    [Fact]
    public async Task HonorsMessagePackAttributesOnReferencedType()
    {
        const string projectA = """
            using MessagePack;

            namespace ProjectA
            {
                [MessagePackObject]
                public class MyObject
                {
                    [Key(0)]
                    public int Id { get; set; }

                    [Key(1)]
                    public string? Name { get; set; }

                    [IgnoreMember]
                    public string? Secret { get; set; }
                }
            }
            """;

        const string projectB = """
            using MessagePack;
            using MessagePack.Formatters;

            [GeneratedMessagePackFormatter]
            public partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject>
            {
            }
            """;

        var result = await GenerateAsync(projectA, projectB, includeMessagePackInProjectA: true);

        result.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        result.CompilerDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        result.Sources.Should().Contain("partial class MyObjectFormatter");
        result.Sources.Should().Contain("WriteArrayHeader(2)");
        result.Sources.Should().NotContain("WriteMapHeader");
        result.Sources.Should().NotContain("Secret");
    }

    private static async Task<(string Sources, ImmutableArray<Diagnostic> GeneratorDiagnostics, ImmutableArray<Diagnostic> CompilerDiagnostics)> GenerateAsync(
        string projectASource,
        string projectBSource,
        bool includeMessagePackInProjectA)
    {
        ImmutableArray<MetadataReference> frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        ImmutableArray<MetadataReference> messagePackReferences = ReferencesHelper.GetReferences(ReferencesSet.MessagePack);
        CSharpParseOptions parseOptions = new(LanguageVersion.Latest);

        ImmutableArray<MetadataReference> projectAReferences = includeMessagePackInProjectA
            ? frameworkReferences.AddRange(messagePackReferences)
            : frameworkReferences;

        CSharpCompilation projectA = CSharpCompilation.Create(
            assemblyName: "ProjectA",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(projectASource, parseOptions) },
            references: projectAReferences,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using MemoryStream image = new();
        var emitResult = projectA.Emit(image);
        emitResult.Success.Should().BeTrue(string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference projectAReference = MetadataReference.CreateFromImage(image.ToArray());

        CSharpCompilation projectB = CSharpCompilation.Create(
            assemblyName: "ProjectB",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(projectBSource, parseOptions) },
            references: frameworkReferences.AddRange(messagePackReferences).Add(projectAReference),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(
            projectB,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> generatorDiagnostics,
            CancellationToken.None);

        string sources = string.Join(
            Environment.NewLine,
            driver.GetRunResult().Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()));

        return (sources, generatorDiagnostics, outputCompilation.GetDiagnostics());
    }
}
