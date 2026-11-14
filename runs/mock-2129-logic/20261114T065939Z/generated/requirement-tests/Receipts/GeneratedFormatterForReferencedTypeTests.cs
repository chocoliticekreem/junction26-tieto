// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterForReferencedTypeTests(ITestOutputHelper logger)
{
    [Fact]
    public async Task BodylessFormatterForAttributelessTypeInReferencedAssembly_IsGenerated()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);

        string projectA = """
            namespace ProjectA
            {
                public class MyType
                {
                    public int Id { get; set; }
                    public string Name { get; set; }
                }
            }
            """;

        // Project A references only the framework, not MessagePack.
        MetadataReference projectARef = EmitAssembly("ProjectA", projectA, framework);

        string projectB = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }

                [GeneratedMessagePackFormatter]
                partial class MyTypeFormatter : IMessagePackFormatter<ProjectA.MyType>;
            }
            """;

        List<MetadataReference> references = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        references.Add(projectARef);

        (string generated, ImmutableArray<Diagnostic> errors) = this.RunGenerator(projectB, references);

        Assert.Empty(errors);
        Assert.Contains("MyTypeFormatter", generated);
        Assert.Contains(".Id", generated);
        Assert.Contains(".Name", generated);
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
