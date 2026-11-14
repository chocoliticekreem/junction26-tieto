// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterHonorsAttributesTests
{
    private const string ProjectBSource = """
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

    [Fact]
    public async Task MessagePackObjectIgnoreMemberOnReferencedTypeIsHonored()
    {
        string projectA = """
            using MessagePack;

            namespace ProjectA
            {
                [MessagePackObject(true)]
                public class MyObject
                {
                    public int Id { get; set; }

                    [IgnoreMember]
                    public string IgnoredSecretValue { get; set; }
                }
            }
            """;

        var (output, generatorDiagnostics, generated) = await RunAsync(projectA, true, ProjectBSource);
        AssertNoErrors(output, generatorDiagnostics);
        Assert.Contains("MyObjectFormatter", generated);
        Assert.DoesNotContain("IgnoredSecretValue", generated);
    }

    [Fact]
    public async Task IntKeysOnReferencedTypeProduceArrayFormat()
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

                    [Key(1)]
                    public string Label { get; set; }
                }
            }
            """;

        var (output, generatorDiagnostics, generated) = await RunAsync(projectA, true, ProjectBSource);
        AssertNoErrors(output, generatorDiagnostics);
        Assert.Contains("MyObjectFormatter", generated);
        Assert.Contains("WriteArrayHeader", generated);
        Assert.DoesNotContain("WriteMapHeader", generated);
    }

    [Fact]
    public async Task DataContractOnReferencedTypeIsHonored()
    {
        string projectA = """
            using System.Runtime.Serialization;

            namespace ProjectA
            {
                [DataContract]
                public class MyObject
                {
                    [DataMember(Order = 0)]
                    public int Id { get; set; }

                    public string NotADataMemberValue { get; set; }
                }
            }
            """;

        var (output, generatorDiagnostics, generated) = await RunAsync(projectA, false, ProjectBSource);
        AssertNoErrors(output, generatorDiagnostics);
        Assert.Contains("MyObjectFormatter", generated);
        Assert.DoesNotContain("NotADataMemberValue", generated);
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
