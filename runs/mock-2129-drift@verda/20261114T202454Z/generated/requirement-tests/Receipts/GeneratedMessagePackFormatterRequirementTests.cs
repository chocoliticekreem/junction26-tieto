// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedMessagePackFormatterRequirementTests
{
    [Fact]
    public async Task FormatterForAttributeFreeTypeInReferencedAssemblyIsGenerated()
    {
        MetadataReference projectA = await CompileLibraryAsync(
            """
            namespace ProjectA;

            public class MyObject
            {
                public int Id { get; set; }

                public string Name { get; set; } = string.Empty;
            }
            """);

        var result = await GenerateProjectBAsync(
            """
            using MessagePack;
            using MessagePack.Formatters;

            [GeneratedMessagePackFormatter]
            public partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject>
            {
            }

            [GeneratedMessagePackResolver]
            public partial class TestResolver
            {
            }
            """,
            projectA);

        AssertNoErrors(result.OutputCompilation, result.GeneratorDiagnostics);
        result.GeneratedSource.Should().Contain("partial class MyObjectFormatter");
        result.GeneratedSource.Should().Contain("Id");
        result.GeneratedSource.Should().Contain("Name");
    }

    [Fact]
    public async Task MessagePackAttributesOnReferencedTypeAreHonored()
    {
        MetadataReference projectA = await CompileLibraryAsync(
            """
            using MessagePack;

            namespace ProjectA;

            [MessagePackObject]
            public class MyObject
            {
                [Key(0)]
                public int Id { get; set; }

                [Key(1)]
                public string Name { get; set; } = string.Empty;

                [IgnoreMember]
                public string DoNotSerialize { get; set; } = string.Empty;
            }
            """,
            ReferencesHelper.GetReferences(ReferencesSet.MessagePack).ToArray());

        var result = await GenerateProjectBAsync(
            """
            using MessagePack;
            using MessagePack.Formatters;

            [GeneratedMessagePackFormatter]
            public partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject>
            {
            }
            """,
            projectA);

        AssertNoErrors(result.OutputCompilation, result.GeneratorDiagnostics);
        result.GeneratedSource.Should().Contain("partial class MyObjectFormatter");
        result.GeneratedSource.Should().Contain("WriteArrayHeader(2)");
        result.GeneratedSource.Should().NotContain("DoNotSerialize");
    }

    [Fact]
    public async Task BodyLessFormatterDeclarationIsSupported()
    {
        var result = await GenerateProjectBAsync(
            """
            using MessagePack;
            using MessagePack.Formatters;

            public class MyObject
            {
                public int Id { get; set; }
            }

            [GeneratedMessagePackFormatter]
            public partial class MyObjectFormatter : IMessagePackFormatter<MyObject>;
            """);

        AssertNoErrors(result.OutputCompilation, result.GeneratorDiagnostics);
        result.GeneratedSource.Should().Contain("partial class MyObjectFormatter");
    }

    private static async Task<MetadataReference> CompileLibraryAsync(string source, params MetadataReference[] additionalReferences)
    {
        ImmutableArray<MetadataReference> references = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        references = references.AddRange(additionalReferences);
        CSharpParseOptions parseOptions = new(LanguageVersion.Preview);
        CSharpCompilation compilation = CSharpCompilation.Create(
            $"ProjectA_{Guid.NewGuid():N}",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        using var image = new MemoryStream();
        var emitResult = compilation.Emit(image);
        emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        return MetadataReference.CreateFromImage(image.ToArray());
    }

    private static async Task<(Compilation OutputCompilation, ImmutableArray<Diagnostic> GeneratorDiagnostics, string GeneratedSource)> GenerateProjectBAsync(
        string source,
        params MetadataReference[] additionalReferences)
    {
        ImmutableArray<MetadataReference> references = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        references = references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        references = references.AddRange(additionalReferences);
        CSharpParseOptions parseOptions = new(LanguageVersion.Preview);
        CSharpCompilation compilation = CSharpCompilation.Create(
            $"ProjectB_{Guid.NewGuid():N}",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> generatorDiagnostics,
            CancellationToken.None);

        string generatedSource = string.Join(
            "\n",
            driver.GetRunResult().Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()));
        return (outputCompilation, generatorDiagnostics, generatedSource);
    }

    private static void AssertNoErrors(Compilation outputCompilation, ImmutableArray<Diagnostic> generatorDiagnostics)
    {
        generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
    }
}
