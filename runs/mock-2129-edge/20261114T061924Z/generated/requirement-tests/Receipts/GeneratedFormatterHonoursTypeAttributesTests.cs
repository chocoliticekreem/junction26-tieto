// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterHonoursTypeAttributesTests
{
    private const string ProjectBSource = """
        using MessagePack;
        using MessagePack.Formatters;

        namespace ProjectB
        {
            [GeneratedMessagePackFormatter]
            public partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject>
            {
            }
        }
        """;

    [Fact]
    public async Task DataContractMembersAreHonoured()
    {
        string projectASource = """
            using System.Runtime.Serialization;

            namespace ProjectA
            {
                [DataContract]
                public class MyObject
                {
                    [DataMember(Order = 0)]
                    public int Id { get; set; }

                    [DataMember(Order = 1)]
                    public string Label { get; set; }

                    public string Secret { get; set; }
                }
            }
            """;

        (string generated, ImmutableArray<Diagnostic> errors) = await RunAsync(projectASource, referenceMessagePackInProjectA: false);
        Assert.Empty(errors);
        Assert.Contains("MyObjectFormatter", generated);
        Assert.DoesNotContain("Secret", generated);
    }

    [Fact]
    public async Task MessagePackObjectAttributesAreHonoured()
    {
        string projectASource = """
            using MessagePack;

            namespace ProjectA
            {
                [MessagePackObject]
                public class MyObject
                {
                    [Key(0)]
                    public int Id { get; set; }

                    [Key(1)]
                    public string Label { get; set; }

                    [IgnoreMember]
                    public string Secret { get; set; }
                }
            }
            """;

        (string generated, ImmutableArray<Diagnostic> errors) = await RunAsync(projectASource, referenceMessagePackInProjectA: true);
        Assert.Empty(errors);
        Assert.Contains("MyObjectFormatter", generated);
        Assert.DoesNotContain("Secret", generated);
    }

    private static async Task<(string Generated, ImmutableArray<Diagnostic> Errors)> RunAsync(string projectASource, bool referenceMessagePackInProjectA)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> messagePack = ReferencesHelper.GetReferences(ReferencesSet.MessagePack).Cast<MetadataReference>().ToList();
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest);

        List<MetadataReference> projectAReferences = new List<MetadataReference>(framework);
        if (referenceMessagePackInProjectA)
        {
            projectAReferences.AddRange(messagePack);
        }

        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(projectASource, parseOptions) },
            projectAReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream peStream = new MemoryStream();
        var emitResult = projectA.Emit(peStream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference projectAReference = MetadataReference.CreateFromImage(peStream.ToArray().ToImmutableArray());

        List<MetadataReference> references = new List<MetadataReference>(framework);
        references.AddRange(messagePack);
        references.Add(projectAReference);

        CSharpCompilation projectB = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(ProjectBSource, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new MessagePackGenerator().AsSourceGenerator() }, parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(projectB, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);

        string generated = string.Join(Environment.NewLine, driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()));
        ImmutableArray<Diagnostic> errors = generatorDiagnostics
            .Concat(output.GetDiagnostics())
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
        return (generated, errors);
    }
}
