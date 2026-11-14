// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterContractlessRulesTests(ITestOutputHelper logger)
{
    private const string ProjectBTemplate = """
        using MessagePack;
        using MessagePack.Formatters;

        namespace ProjectB
        {
            [GeneratedMessagePackResolver]
            partial class MyResolver
            {
            }

            [GeneratedMessagePackFormatter]
            partial class TheFormatter : IMessagePackFormatter<ProjectA.TheType>
            {
            }
        }
        """;

    [Fact]
    public async Task PublicMutableFieldIsSerializedAndReadonlyFieldDoesNotBreakBuild()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);

        string projectA = """
            namespace ProjectA
            {
                public class TheType
                {
                    public int Counter;
                    public readonly int Fixed = 3;
                    public string Label { get; set; }
                }
            }
            """;
        MetadataReference projectARef = EmitAssembly("ProjectA", projectA, framework);

        List<MetadataReference> references = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        references.Add(projectARef);

        (string generated, ImmutableArray<Diagnostic> errors) = this.RunGenerator(ProjectBTemplate, references);

        Assert.Empty(errors);
        Assert.Contains(".Counter", generated);
        Assert.Contains(".Label", generated);
    }

    [Fact]
    public async Task MessagePackAttributesOnFormattedTypeAreHonoured()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> withMessagePack = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();

        string projectA = """
            using MessagePack;

            namespace ProjectA
            {
                [MessagePackObject]
                public class TheType
                {
                    [Key(0)]
                    public int Id { get; set; }

                    [Key(1)]
                    public string Name { get; set; }

                    [IgnoreMember]
                    public string SkipMePlease { get; set; }
                }
            }
            """;
        MetadataReference projectARef = EmitAssembly("ProjectA", projectA, withMessagePack);

        List<MetadataReference> references = new(withMessagePack) { projectARef };

        (string generated, ImmutableArray<Diagnostic> errors) = this.RunGenerator(ProjectBTemplate, references);

        Assert.Empty(errors);
        Assert.Contains("TheFormatter", generated);
        Assert.Contains("WriteArrayHeader(", generated);
        Assert.DoesNotContain("WriteMapHeader(", generated);
        Assert.DoesNotContain("SkipMePlease", generated);
    }

    private static MetadataReference EmitAssembly(string assemblyName, string source, IEnumerable<MetadataReference> references)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private (string GeneratedText, ImmutableArray<Diagnostic> Errors) RunGenerator(string source, IEnumerable<MetadataReference> references)
    {
        CSharpParseOptions parseOptions = new(LanguageVersion.CSharp12);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        string generated = string.Join(Environment.NewLine, driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()));
        logger.WriteLine(generated);
        ImmutableArray<Diagnostic> errors = generatorDiagnostics.Concat(output.GetDiagnostics())
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
        foreach (Diagnostic diagnostic in errors)
        {
            logger.WriteLine(diagnostic.ToString());
        }

        return (generated, errors);
    }
}
