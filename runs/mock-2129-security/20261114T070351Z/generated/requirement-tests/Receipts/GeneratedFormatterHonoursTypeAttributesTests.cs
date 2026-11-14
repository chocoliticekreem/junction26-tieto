// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterHonoursTypeAttributesTests
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp12);

    private const string ProjectASource = """
        using MessagePack;

        namespace ProjectA
        {
            [MessagePackObject]
            public class Annotated
            {
                [Key(0)]
                public int OrderNumber { get; set; }

                [IgnoreMember]
                public string SecretToken { get; set; }
            }
        }
        """;

    private const string ProjectBSource = """
        using MessagePack;
        using MessagePack.Formatters;

        namespace ProjectB
        {
            [GeneratedMessagePackFormatter]
            public partial class AnnotatedFormatter : IMessagePackFormatter<ProjectA.Annotated>
            {
            }

            [GeneratedMessagePackResolver]
            partial class MyResolver
            {
            }
        }
        """;

    [Fact]
    public async Task IgnoreMemberOnReferencedType_IsNotSerialized()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> messagePackReferences = new(framework);
        messagePackReferences.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        MetadataReference projectA = Emit("ProjectA", ProjectASource, messagePackReferences);

        List<MetadataReference> projectBReferences = new(messagePackReferences);
        projectBReferences.Add(projectA);

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(ProjectBSource, ParseOptions) },
            projectBReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
        GeneratorDriverRunResult run = driver.GetRunResult();
        string generated = string.Join("\n", run.GeneratedTrees.Select(t => t.ToString()));

        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error || d.Id == "CS8785");
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("AnnotatedFormatter", generated);
        Assert.Contains("OrderNumber", generated);
        Assert.DoesNotContain("SecretToken", generated);
    }

    private static MetadataReference Emit(string assemblyName, string source, IEnumerable<MetadataReference> references)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }
}
