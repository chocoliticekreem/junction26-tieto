// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterForReferencedTypeTests
{
    private const string ProjectAPlain = """
        namespace ProjectA
        {
            public class MyType
            {
                public int Id { get; set; }

                public string Name { get; set; }
            }
        }
        """;

    private static readonly CSharpParseOptions LatestParseOptions = new(LanguageVersion.Latest);

    private readonly ITestOutputHelper logger;

    public GeneratedFormatterForReferencedTypeTests(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    [Fact]
    public async Task BodylessFormatterForAttributeFreeReferencedType_GeneratesAndCompiles()
    {
        List<MetadataReference> frameworkOnly = await GetReferencesAsync(includeMessagePack: false);
        MetadataReference projectA = CompileProjectA(ProjectAPlain, frameworkOnly);

        List<MetadataReference> references = await GetReferencesAsync(includeMessagePack: true);
        references.Add(projectA);

        string projectB = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver { }

                [GeneratedMessagePackFormatter]
                partial class MyTypeFormatter : IMessagePackFormatter<ProjectA.MyType>;
            }
            """;

        (List<Diagnostic> errors, List<string> generated) = this.RunGenerator(projectB, references);
        Assert.Empty(errors);
        Assert.Contains(generated, s => s.Contains("MyTypeFormatter"));
    }

    [Fact]
    public async Task FormatterWithoutResolverDeclaration_GeneratesAndCompiles()
    {
        List<MetadataReference> frameworkOnly = await GetReferencesAsync(includeMessagePack: false);
        MetadataReference projectA = CompileProjectA(ProjectAPlain, frameworkOnly);

        List<MetadataReference> references = await GetReferencesAsync(includeMessagePack: true);
        references.Add(projectA);

        string projectB = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                partial class MyTypeFormatter : IMessagePackFormatter<ProjectA.MyType>;
            }
            """;

        (List<Diagnostic> errors, List<string> generated) = this.RunGenerator(projectB, references);
        Assert.Empty(errors);
        Assert.Contains(generated, s => s.Contains("MyTypeFormatter"));
    }

    [Fact]
    public async Task AttributedReferencedType_IgnoreMemberIsHonoured()
    {
        List<MetadataReference> withMessagePack = await GetReferencesAsync(includeMessagePack: true);
        string projectASource = """
            using MessagePack;

            namespace ProjectA
            {
                [MessagePackObject]
                public class MyType
                {
                    [Key(0)]
                    public int Id { get; set; }

                    [IgnoreMember]
                    public string Secret { get; set; }
                }
            }
            """;
        MetadataReference projectA = CompileProjectA(projectASource, withMessagePack);

        List<MetadataReference> references = await GetReferencesAsync(includeMessagePack: true);
        references.Add(projectA);

        string projectB = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver { }

                [GeneratedMessagePackFormatter]
                partial class MyTypeFormatter : IMessagePackFormatter<ProjectA.MyType>;
            }
            """;

        (List<Diagnostic> errors, List<string> generated) = this.RunGenerator(projectB, references);
        Assert.Empty(errors);
        Assert.Contains(generated, s => s.Contains("MyTypeFormatter"));
        Assert.DoesNotContain(generated, s => s.Contains("Secret"));
    }

    private static async Task<List<MetadataReference>> GetReferencesAsync(bool includeMessagePack)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = new(framework);
        if (includeMessagePack)
        {
            references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        }

        return references;
    }

    private static MetadataReference CompileProjectA(string source, List<MetadataReference> references)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(source, LatestParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.ToString())));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private (List<Diagnostic> Errors, List<string> Generated) RunGenerator(string source, List<MetadataReference> references)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(source, LatestParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: LatestParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);

        List<string> generated = driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()).ToList();
        foreach (string text in generated)
        {
            this.logger.WriteLine(text);
        }

        List<Diagnostic> errors = generatorDiagnostics.Concat(output.GetDiagnostics())
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        foreach (Diagnostic diagnostic in errors)
        {
            this.logger.WriteLine(diagnostic.ToString());
        }

        return (errors, generated);
    }
}
