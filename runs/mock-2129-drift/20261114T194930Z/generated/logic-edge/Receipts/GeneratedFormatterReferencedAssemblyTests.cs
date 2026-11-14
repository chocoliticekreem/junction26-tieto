// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterReferencedAssemblyTests
{
    private readonly ITestOutputHelper logger;

    public GeneratedFormatterReferencedAssemblyTests(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    [Fact]
    public async Task FormatterIsGeneratedForTypeFromReferencedAssembly()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        CSharpCompilationOptions compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable);

        string projectASource = "namespace ProjectA { public class MyType { public int Id { get; set; } public string? Name { get; set; } } }";
        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(projectASource, parseOptions) },
            framework,
            compilationOptions);

        using MemoryStream image = new();
        var emitResult = projectA.Emit(image);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference projectARef = MetadataReference.CreateFromImage(image.ToArray());

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

        List<MetadataReference> references = new();
        references.AddRange(framework);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        references.Add(projectARef);

        CSharpCompilation projectB = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(projectBSource, parseOptions) },
            references,
            compilationOptions);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(projectB, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);

        foreach (Diagnostic d in generatorDiagnostics)
        {
            this.logger.WriteLine("generator: " + d);
        }

        foreach (SyntaxTree tree in output.SyntaxTrees)
        {
            this.logger.WriteLine("--- " + tree.FilePath);
            this.logger.WriteLine(tree.ToString());
        }

        Assert.DoesNotContain(generatorDiagnostics, d => d.Id == "MsgPack021");

        List<Diagnostic> errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        foreach (Diagnostic d in errors)
        {
            this.logger.WriteLine("compile: " + d);
        }

        Assert.Empty(errors);
    }
}
