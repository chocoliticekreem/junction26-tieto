// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterLogicReceipts
{
    [Fact]
    public async Task MetadataType_MessagePackKeys_AreHonored()
    {
        MetadataReference projectA = await CompileMetadataLibraryAsync("""
            using MessagePack;

            [MessagePackObject]
            public class ProjectAType
            {
                [Key(0)]
                public int Value { get; set; }

                [IgnoreMember]
                public int Ignored { get; set; }
            }
            """);

        GenerationResult generation = await GenerateAsync("""
            using MessagePack;
            using MessagePack.Formatters;

            [GeneratedMessagePackFormatter]
            public partial class ProjectATypeFormatter : IMessagePackFormatter<ProjectAType>
            {
            }
            """, projectA);

        Assert.DoesNotContain(generation.GeneratorDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        ImmutableArray<Diagnostic> compileErrors = GetCompileErrors(generation.OutputCompilation);
        Assert.True(compileErrors.IsEmpty, string.Join("\n", compileErrors.Select(d => d.ToString())));
        Assert.Contains("WriteArrayHeader", generation.GeneratedText);
        Assert.DoesNotContain("WriteMapHeader", generation.GeneratedText);
        Assert.DoesNotContain("\"Ignored\"", generation.GeneratedText);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public async Task RejectsInvalidSurrogateIntegerKeys(int key)
    {
        GenerationResult generation = await GenerateAsync($$"""
            using MessagePack;
            using MessagePack.Formatters;

            public class ProjectAType
            {
                public int Value { get; set; }
            }

            public class ProjectATypeSurrogate
            {
                [Key({{key}})]
                public int Value { get; set; }
            }

            [GeneratedMessagePackFormatter(SurrogateType = typeof(ProjectATypeSurrogate))]
            public partial class ProjectATypeFormatter : IMessagePackFormatter<ProjectAType>
            {
            }
            """);

        Assert.Contains(
            generation.GeneratorDiagnostics,
            d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("MsgPack", StringComparison.Ordinal));
    }

    [Fact]
    public async Task NestedFormatter_InNonPartialContainer_GeneratesCompilableCode()
    {
        GenerationResult generation = await GenerateAsync("""
            using MessagePack;
            using MessagePack.Formatters;

            public class ProjectAType
            {
                public int Value { get; set; }
            }

            public class Container
            {
                [GeneratedMessagePackFormatter]
                public partial class NestedFormatter : IMessagePackFormatter<ProjectAType>
                {
                }
            }
            """);

        Assert.DoesNotContain(generation.GeneratorDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        ImmutableArray<Diagnostic> compileErrors = GetCompileErrors(generation.OutputCompilation);
        Assert.True(compileErrors.IsEmpty, string.Join("\n", compileErrors.Select(d => d.ToString())));
    }

    private static async Task<MetadataReference> CompileMetadataLibraryAsync(string source)
    {
        List<MetadataReference> references = await GetMessagePackReferencesAsync();
        CSharpCompilation library = CreateCompilation("ProjectA", source, references);
        using MemoryStream peStream = new();
        Microsoft.CodeAnalysis.Emit.EmitResult emitResult = library.Emit(peStream);
        Assert.True(emitResult.Success, string.Join("\n", emitResult.Diagnostics.Select(d => d.ToString())));
        return MetadataReference.CreateFromImage(peStream.ToArray());
    }

    private static async Task<GenerationResult> GenerateAsync(string source, MetadataReference? additionalReference = null)
    {
        List<MetadataReference> references = await GetMessagePackReferencesAsync();
        if (additionalReference is not null)
        {
            references.Add(additionalReference);
        }

        CSharpCompilation compilation = CreateCompilation("ProjectB", source, references);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out _);
        GeneratorDriverRunResult runResult = driver.GetRunResult();
        string generatedText = string.Join(
            Environment.NewLine,
            runResult.Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()));
        return new GenerationResult(outputCompilation, runResult.Diagnostics, generatedText);
    }

    private static async Task<List<MetadataReference>> GetMessagePackReferencesAsync()
    {
        List<MetadataReference> references = (await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None)).ToList();
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        return references;
    }

    private static CSharpCompilation CreateCompilation(string assemblyName, string source, IEnumerable<MetadataReference> references)
    {
        return CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    private static ImmutableArray<Diagnostic> GetCompileErrors(Compilation compilation)
    {
        return compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray();
    }

    private sealed record GenerationResult(
        Compilation OutputCompilation,
        ImmutableArray<Diagnostic> GeneratorDiagnostics,
        string GeneratedText);
}
