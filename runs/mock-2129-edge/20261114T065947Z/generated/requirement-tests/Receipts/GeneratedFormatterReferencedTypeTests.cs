// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterReferencedTypeTests
{
    private const string ProjectASource = """
        namespace ProjectA
        {
            public class MyObject
            {
                public int Id { get; set; }

                public string Label { get; set; }
            }
        }
        """;

    [Fact]
    public async Task FormatterForReferencedTypeWithoutAttributesIsGenerated()
    {
        string projectB = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver { }

                [GeneratedMessagePackFormatter]
                public partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject>
                {
                }
            }
            """;

        var (output, generatorDiagnostics, generated) = await RunAsync(ProjectASource, false, projectB);
        AssertNoErrors(output, generatorDiagnostics);
        Assert.Contains("MyObjectFormatter", generated);
        Assert.Contains("Label", generated);
        Assert.Contains("Id", generated);
    }

    [Fact]
    public async Task BodylessFormatterDeclarationCompiles()
    {
        string projectB = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver { }

                [GeneratedMessagePackFormatter]
                public partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject>;
            }
            """;

        var (output, generatorDiagnostics, generated) = await RunAsync(ProjectASource, false, projectB);
        AssertNoErrors(output, generatorDiagnostics);
        Assert.Contains("MyObjectFormatter", generated);
    }

    private static void AssertNoErrors(Compilation output, ImmutableArray<Diagnostic> generatorDiagnostics)
    {
        List<Diagnostic> errors = output.GetDiagnostics().Concat(generatorDiagnostics).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    private static async Task<(Compilation Output, ImmutableArray<Diagnostic> GeneratorDiagnostics, string Generated)> RunAsync(string projectASource, bool projectAReferencesMessagePack, string projectBSource)
    {
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> messagePack = ReferencesHelper.GetReferences(ReferencesSet.MessagePack).Cast<MetadataReference>().ToList();

        List<MetadataReference> referencesA = framework.ToList();
        if (projectAReferencesMessagePack)
        {
            referencesA.AddRange(messagePack);
        }

        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(projectASource, parseOptions) },
            referencesA,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream image = new MemoryStream();
        EmitResult emitResult = projectA.Emit(image);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference projectAReference = MetadataReference.CreateFromImage(image.ToArray());

        List<MetadataReference> referencesB = framework.ToList();
        referencesB.AddRange(messagePack);
        referencesB.Add(projectAReference);
        CSharpCompilation projectB = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(projectBSource, parseOptions) },
            referencesB,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(projectB, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        GeneratorDriverRunResult runResult = driver.GetRunResult();
        foreach (GeneratorRunResult result in runResult.Results)
        {
            Assert.Null(result.Exception);
        }

        string generated = string.Join(Environment.NewLine, runResult.GeneratedTrees.Select(t => t.ToString()));
        return (output, generatorDiagnostics, generated);
    }
}
