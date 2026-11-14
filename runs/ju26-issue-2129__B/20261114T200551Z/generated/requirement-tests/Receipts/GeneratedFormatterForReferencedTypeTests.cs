// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterForReferencedTypeTests
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp12);

    private const string ProjectBSource = """
        using MessagePack;
        using MessagePack.Formatters;

        namespace ProjectB
        {
            [GeneratedMessagePackFormatter]
            partial class MyTypeFormatter : IMessagePackFormatter<ProjectA.MyType>;

            [GeneratedMessagePackResolver]
            partial class MyResolver
            {
            }
        }
        """;

    [Fact]
    public async Task BodylessFormatterForTypeInReferencedAssemblyWithoutMessagePackGetsGeneratedBody()
    {
        string projectA = """
            namespace ProjectA
            {
                public class MyType
                {
                    public int Id { get; set; }
                    public string DisplayLabel { get; set; }
                }
            }
            """;

        (Compilation output, GeneratorDriverRunResult run) = await GenerateAsync(projectA, ProjectBSource);

        Diagnostic[] generatorErrors = run.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(generatorErrors.Length == 0, string.Join(Environment.NewLine, generatorErrors.Select(d => d.ToString())));

        Diagnostic[] errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(d => d.ToString())));

        INamedTypeSymbol? formatter = output.GetTypeByMetadataName("ProjectB.MyTypeFormatter");
        Assert.NotNull(formatter);
        Assert.NotEmpty(formatter!.GetMembers("Serialize"));
        Assert.NotEmpty(formatter.GetMembers("Deserialize"));
    }

    [Fact]
    public async Task ContractlessFormatterCoversPublicMembersOfUnattributedReferencedType()
    {
        string projectA = """
            namespace ProjectA
            {
                public class MyType
                {
                    public int ItemCount;
                    public string DisplayLabel { get; set; }
                }
            }
            """;

        (Compilation output, GeneratorDriverRunResult run) = await GenerateAsync(projectA, ProjectBSource);

        Diagnostic[] errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(d => d.ToString())));

        string generated = string.Join(Environment.NewLine, run.GeneratedTrees.Select(t => t.ToString()));
        Assert.Contains("MyTypeFormatter", generated);
        Assert.Contains("ItemCount", generated);
        Assert.Contains("DisplayLabel", generated);
    }

    private static async Task<(Compilation Output, GeneratorDriverRunResult Run)> GenerateAsync(string projectASource, string projectBSource)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        MetadataReference[] messagePack =
        {
            MetadataReference.CreateFromFile(typeof(global::MessagePack.MessagePackObjectAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(global::MessagePack.MessagePackSerializer).Assembly.Location),
        };

        // Project A has no reference to MessagePack at all.
        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(projectASource, ParseOptions) },
            framework,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        var emitResult = projectA.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics.Select(d => d.ToString())));
        MetadataReference projectARef = MetadataReference.CreateFromImage(stream.ToArray());

        CSharpCompilation projectB = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(projectBSource, ParseOptions) },
            framework.AddRange(messagePack).Add(projectARef),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(projectB, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        return (output, driver.GetRunResult());
    }
}
