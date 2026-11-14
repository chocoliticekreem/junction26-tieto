// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterContractlessFidelityTests
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp12);

    private const string ProjectBSource = """
        using MessagePack;
        using MessagePack.Formatters;

        namespace ProjectB
        {
            [GeneratedMessagePackFormatter]
            partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject>;

            [GeneratedMessagePackResolver]
            partial class MyResolver
            {
            }
        }
        """;

    [Fact]
    public async Task PublicMutableField_IsSerializedInContractlessMode()
    {
        string projectA = """
            namespace ProjectA
            {
                public class MyObject
                {
                    public int Id { get; set; }
                    public string FieldMember;
                }
            }
            """;

        var result = await RunAsync(projectA, projectAReferencesMessagePack: false);

        Assert.True(result.Errors.IsEmpty, string.Join(Environment.NewLine, result.Errors));
        Assert.Contains("partial class MyObjectFormatter", result.GeneratedText);
        Assert.Contains("FieldMember", result.GeneratedText);
    }

    [Fact]
    public async Task DataContractAttributesOnReferencedType_AreHonoured()
    {
        string projectA = """
            using System.Runtime.Serialization;

            namespace ProjectA
            {
                [DataContract]
                public class MyObject
                {
                    [DataMember(Name = "id")]
                    public int Id { get; set; }

                    public string NotADataMember { get; set; }
                }
            }
            """;

        var result = await RunAsync(projectA, projectAReferencesMessagePack: false);

        Assert.True(result.Errors.IsEmpty, string.Join(Environment.NewLine, result.Errors));
        Assert.Contains("partial class MyObjectFormatter", result.GeneratedText);
        Assert.DoesNotContain("NotADataMember", result.GeneratedText);
    }

    [Fact]
    public async Task MessagePackAttributesOnReferencedType_AreHonoured()
    {
        string projectA = """
            using MessagePack;

            namespace ProjectA
            {
                [MessagePackObject]
                public class MyObject
                {
                    [Key(0)]
                    public int Id { get; set; }

                    [IgnoreMember]
                    public string IgnoredByAttribute { get; set; }
                }
            }
            """;

        var result = await RunAsync(projectA, projectAReferencesMessagePack: true);

        Assert.True(result.Errors.IsEmpty, string.Join(Environment.NewLine, result.Errors));
        Assert.Contains("partial class MyObjectFormatter", result.GeneratedText);
        Assert.DoesNotContain("IgnoredByAttribute", result.GeneratedText);
    }

    private static async Task<(ImmutableArray<Diagnostic> Errors, string GeneratedText)> RunAsync(string projectASource, bool projectAReferencesMessagePack)
    {
        ImmutableArray<MetadataReference> framework = await Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);

        List<MetadataReference> projectAReferences = new(framework);
        if (projectAReferencesMessagePack)
        {
            projectAReferences.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        }

        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(projectASource, ParseOptions) },
            projectAReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream image = new();
        var emitResult = projectA.Emit(image);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference projectAReference = MetadataReference.CreateFromImage(image.ToArray());

        List<MetadataReference> references = new(framework);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        references.Add(projectAReference);

        CSharpCompilation projectB = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(ProjectBSource, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(projectB, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);

        GeneratorDriverRunResult run = driver.GetRunResult();
        string text = string.Join(Environment.NewLine, run.GeneratedTrees.Select(t => t.ToString()));
        ImmutableArray<Diagnostic> errors = generatorDiagnostics
            .Concat(output.GetDiagnostics())
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
        return (errors, text);
    }
}
