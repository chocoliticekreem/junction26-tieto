// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
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

            [GeneratedMessagePackResolver]
            partial class MyResolver { }
        }
        """;

    [Fact]
    public async Task ReferencedTypeWithMessagePackObject_IgnoreMemberIsHonoured()
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

                    [IgnoreMember]
                    public string Secret { get; set; }
                }
            }
            """;

        var (output, runResult) = await RunGeneratorAsync(projectASource, projectAReferencesMessagePack: true);

        AssertFormatterGenerated(output);
        Assert.DoesNotContain(runResult.GeneratedTrees, t => t.GetText().ToString().Contains("Secret"));
    }

    [Fact]
    public async Task ReferencedTypeWithDataContract_UnmarkedMemberIsNotSerialized()
    {
        string projectASource = """
            using System.Runtime.Serialization;

            namespace ProjectA
            {
                [DataContract]
                public class MyObject
                {
                    [DataMember]
                    public int Id { get; set; }

                    public string Secret { get; set; }
                }
            }
            """;

        var (output, runResult) = await RunGeneratorAsync(projectASource, projectAReferencesMessagePack: false);

        AssertFormatterGenerated(output);
        Assert.DoesNotContain(runResult.GeneratedTrees, t => t.GetText().ToString().Contains("Secret"));
    }

    private static void AssertFormatterGenerated(Compilation output)
    {
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        INamedTypeSymbol? formatter = output.GetTypeByMetadataName("ProjectB.MyObjectFormatter");
        Assert.NotNull(formatter);
        Assert.NotEmpty(formatter!.GetMembers("Serialize"));
    }

    private static async Task<(Compilation Output, GeneratorDriverRunResult RunResult)> RunGeneratorAsync(string projectASource, bool projectAReferencesMessagePack)
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
            new[] { CSharpSyntaxTree.ParseText(ProjectBSource, parseOptions) },
            projectBReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(projectB, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        return (output, driver.GetRunResult());
    }
}
