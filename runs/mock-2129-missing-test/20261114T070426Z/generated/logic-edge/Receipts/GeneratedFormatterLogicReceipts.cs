// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterLogicReceipts
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    private readonly ITestOutputHelper logger;

    public GeneratedFormatterLogicReceipts(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    [Fact]
    public async Task ReferencedTypeIgnoreMemberIsHonoured()
    {
        string projectA = """
            using MessagePack;
            namespace ProjectA
            {
                [MessagePackObject]
                public class AnnotatedType
                {
                    [Key(0)]
                    public int Visible { get; set; }

                    [IgnoreMember]
                    public int IgnoredSecret { get; set; }
                }
            }
            """;
        string formatterText = await this.GenerateFormatterForReferencedTypeAsync(projectA, "AnnotatedType");
        Assert.Contains("Visible", formatterText);
        Assert.DoesNotContain("IgnoredSecret", formatterText);
    }

    [Fact]
    public async Task ReferencedTypeIntKeysAreHonoured()
    {
        string projectA = """
            using MessagePack;
            namespace ProjectA
            {
                [MessagePackObject]
                public class KeyedType
                {
                    [Key(0)]
                    public int Zeta { get; set; }

                    [Key(1)]
                    public int Alpha { get; set; }
                }
            }
            """;
        string formatterText = await this.GenerateFormatterForReferencedTypeAsync(projectA, "KeyedType");
        Assert.Contains("WriteArrayHeader", formatterText);
        Assert.DoesNotContain("WriteMapHeader", formatterText);
    }

    [Fact]
    public async Task FormatterNestedInNonPartialTypeIsDiagnosedInsteadOfEmittingBrokenCode()
    {
        string projectB = """
            using MessagePack;
            using MessagePack.Formatters;
            namespace ProjectB
            {
                public class Payload
                {
                    public int Value { get; set; }
                }

                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }

                public class Outer
                {
                    [GeneratedMessagePackFormatter]
                    internal partial class PayloadFormatter : IMessagePackFormatter<Payload>
                    {
                    }
                }
            }
            """;
        List<MetadataReference> references = await GetReferencesAsync();
        var (_, generatorDiagnostics, errors) = this.RunGenerator(projectB, references);
        bool diagnosed = generatorDiagnostics.Any(d => d.Id.StartsWith("MsgPack", StringComparison.Ordinal));
        Assert.True(diagnosed || errors.Length == 0, "Expected an MsgPack diagnostic for a formatter nested in a non-partial type, but the generator emitted code with errors: " + string.Join(Environment.NewLine, errors.Select(e => e.ToString())));
    }

    private static async Task<List<MetadataReference>> GetReferencesAsync()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = new(framework);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        return references;
    }

    private async Task<string> GenerateFormatterForReferencedTypeAsync(string projectASource, string typeName)
    {
        List<MetadataReference> references = await GetReferencesAsync();
        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(projectASource, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        var emitResult = projectA.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics.Select(d => d.ToString())));
        references.Add(MetadataReference.CreateFromImage(stream.ToArray()));

        string projectB = $$"""
            using MessagePack;
            using MessagePack.Formatters;
            namespace ProjectB
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }

                [GeneratedMessagePackFormatter]
                partial class {{typeName}}Formatter : IMessagePackFormatter<ProjectA.{{typeName}}>
                {
                }
            }
            """;
        var (generated, _, _) = this.RunGenerator(projectB, references);
        string? text = generated.FirstOrDefault(t => t.Contains($"class {typeName}Formatter", StringComparison.Ordinal));
        Assert.True(text is not null, $"No generated formatter source for {typeName}Formatter was found.");
        return text!;
    }

    private (string[] Generated, ImmutableArray<Diagnostic> GeneratorDiagnostics, Diagnostic[] OutputErrors) RunGenerator(string source, IEnumerable<MetadataReference> references)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        GeneratorDriverRunResult runResult = driver.GetRunResult();
        foreach (GeneratorRunResult result in runResult.Results)
        {
            if (result.Exception is not null)
            {
                this.logger.WriteLine("Generator exception: " + result.Exception);
            }
        }

        string[] generated = runResult.GeneratedTrees.Select(t => t.ToString()).ToArray();
        foreach (string g in generated)
        {
            this.logger.WriteLine(g);
        }

        foreach (Diagnostic d in generatorDiagnostics)
        {
            this.logger.WriteLine("Generator diagnostic: " + d);
        }

        Diagnostic[] errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        foreach (Diagnostic e in errors)
        {
            this.logger.WriteLine("Output error: " + e);
        }

        return (generated, generatorDiagnostics, errors);
    }
}
