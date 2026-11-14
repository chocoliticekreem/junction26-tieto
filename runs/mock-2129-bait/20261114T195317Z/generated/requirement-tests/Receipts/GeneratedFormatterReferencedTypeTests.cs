// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
    public async Task BodylessFormatterForReferencedType_IsGeneratedAndCompiles()
    {
        string projectBSource = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject>;

                [GeneratedMessagePackResolver]
                partial class MyResolver { }
            }
            """;

        var (output, generatorDiagnostics, runResult) = await RunGeneratorAsync(ProjectASource, projectAReferencesMessagePack: false, projectBSource);

        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        INamedTypeSymbol? formatter = output.GetTypeByMetadataName("ProjectB.MyObjectFormatter");
        Assert.NotNull(formatter);
        Assert.NotEmpty(formatter!.GetMembers("Serialize"));
        Assert.NotEmpty(formatter.GetMembers("Deserialize"));
    }

    [Fact]
    public async Task ContractlessFormatterForReferencedType_UsesPublicMembers()
    {
        string projectBSource = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                public partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject>
                {
                }

                [GeneratedMessagePackResolver]
                partial class MyResolver { }
            }
            """;

        var (output, generatorDiagnostics, runResult) = await RunGeneratorAsync(ProjectASource, projectAReferencesMessagePack: false, projectBSource);

        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        string formatterSource = runResult.GeneratedTrees
            .Select(t => t.GetText().ToString())
            .FirstOrDefault(s => s.Contains("MyObjectFormatter") && s.Contains("Serialize")) ?? string.Empty;
        Assert.Contains("Id", formatterSource);
        Assert.Contains("Label", formatterSource);
    }

    private static async Task<(Compilation Output, ImmutableArray<Diagnostic> GeneratorDiagnostics, GeneratorDriverRunResult RunResult)> RunGeneratorAsync(string projectASource, bool projectAReferencesMessagePack, string projectBSource)
    {
        CSharpParseOptions parseOptions = new(LanguageVersion.CSharp12);
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> messagePack = ReferencesHelper.GetReferences(ReferencesSet.MessagePack).Cast<MetadataReference>().ToList();

        List<MetadataReference> projectAReferences = new(framework);
        if (projectAReferencesMessagePack)
        {
            projectAReferences.AddRange(messagePack);
        }

        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(projectASource, parseOptions) },
            projectAReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream peStream = new();
        EmitResult emitResult = projectA.Emit(peStream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference projectAReference = MetadataReference.CreateFromImage(peStream.ToArray());

        List<MetadataReference> projectBReferences = new(framework);
        projectBReferences.AddRange(messagePack);
        projectBReferences.Add(projectAReference);
        CSharpCompilation projectB = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(projectBSource, parseOptions) },
            projectBReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(projectB, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        return (output, generatorDiagnostics, driver.GetRunResult());
    }
}
