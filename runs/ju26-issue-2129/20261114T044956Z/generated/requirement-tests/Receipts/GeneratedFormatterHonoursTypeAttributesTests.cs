// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterHonoursTypeAttributesTests(ITestOutputHelper logger)
{
    private const string ProjectBSource = """
        using MessagePack;
        using MessagePack.Formatters;

        namespace ProjectB
        {
            [GeneratedMessagePackFormatter]
            public partial class MyTypeFormatter : IMessagePackFormatter<ProjectA.MyType>
            {
            }

            [GeneratedMessagePackResolver]
            partial class MyResolver
            {
            }
        }
        """;

    [Fact]
    public async Task DataContractOnReferencedTypeIsHonoured()
    {
        string projectA = """
            using System.Runtime.Serialization;

            namespace ProjectA
            {
                [DataContract]
                public class MyType
                {
                    [DataMember(Order = 0)]
                    public int Id { get; set; }

                    public string Secret { get; set; }
                }
            }
            """;

        string formatter = await this.GetGeneratedFormatterAsync(projectA, referenceAnnotations: false);
        Assert.Contains("Id", formatter);
        Assert.DoesNotContain("Secret", formatter);
    }

    [Fact]
    public async Task MessagePackAttributesOnReferencedTypeAreHonoured()
    {
        string projectA = """
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

        string formatter = await this.GetGeneratedFormatterAsync(projectA, referenceAnnotations: true);
        Assert.Contains("Id", formatter);
        Assert.DoesNotContain("Secret", formatter);
    }

    private async Task<string> GetGeneratedFormatterAsync(string projectASource, bool referenceAnnotations)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        MetadataReference annotations = MetadataReference.CreateFromFile(typeof(MessagePack.MessagePackObjectAttribute).Assembly.Location);
        IEnumerable<MetadataReference> projectAReferences = referenceAnnotations ? framework.Concat(new[] { annotations }) : framework;
        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(projectASource) },
            projectAReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        var emitResult = projectA.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference projectARef = MetadataReference.CreateFromImage(stream.ToArray());

        CSharpParseOptions parseOptions = new(LanguageVersion.CSharp12);
        IEnumerable<MetadataReference> references = framework.Concat(new MetadataReference[]
        {
            annotations,
            MetadataReference.CreateFromFile(typeof(MessagePack.MessagePackSerializer).Assembly.Location),
            projectARef,
        });
        CSharpCompilation projectB = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(ProjectBSource, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(projectB, out Compilation output, out _);
        foreach (Diagnostic error in output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error))
        {
            logger.WriteLine(error.ToString());
        }

        List<string> generated = driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()).ToList();
        foreach (string text in generated)
        {
            logger.WriteLine(text);
        }

        string? formatter = generated.FirstOrDefault(s => s.Contains("class MyTypeFormatter"));
        Assert.NotNull(formatter);
        return formatter!;
    }
}
