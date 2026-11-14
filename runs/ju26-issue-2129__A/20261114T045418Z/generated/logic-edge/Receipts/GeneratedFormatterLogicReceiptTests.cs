// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterLogicReceiptTests
{
    private static async Task<(GeneratorDriverRunResult Result, Compilation Output)> RunAsync(string projectASource, string projectBSource)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        CSharpParseOptions parseOptions = new(LanguageVersion.Latest);

        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(projectASource, parseOptions) },
            framework,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        var emitResult = projectA.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics.Select(d => d.ToString())));
        MetadataReference projectARef = MetadataReference.CreateFromImage(stream.ToArray());

        List<MetadataReference> references = new();
        references.AddRange(framework);
        foreach (MetadataReference r in ReferencesHelper.GetReferences(ReferencesSet.MessagePack))
        {
            references.Add(r);
        }

        references.Add(projectARef);

        CSharpCompilation projectB = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(projectBSource, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(projectB, out Compilation output, out ImmutableArray<Diagnostic> _);
        return (driver.GetRunResult(), output);
    }

    [Fact]
    public async Task DataContractOnReferencedTypeIsHonoured()
    {
        string projectA = """
            using System.Runtime.Serialization;

            namespace ProjectA
            {
                [DataContract]
                public class MyObject
                {
                    [DataMember(Name = "a")]
                    public int Included { get; set; }

                    public int NotAMember { get; set; }
                }
            }
            """;
        string projectB = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver { }

                [GeneratedMessagePackFormatter]
                partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject> { }
            }
            """;

        var (result, _) = await RunAsync(projectA, projectB);
        List<string> generated = result.GeneratedTrees.Select(t => t.ToString()).ToList();

        Assert.Contains(generated, s => s.Contains("MyObjectFormatter") && s.Contains("Serialize"));
        Assert.DoesNotContain(generated, s => s.Contains("NotAMember"));
    }

    [Fact]
    public async Task FormatterNestedInNonPartialTypeDoesNotProduceCS0260()
    {
        string projectA = """
            namespace ProjectA
            {
                public class MyObject
                {
                    public int Value { get; set; }
                }
            }
            """;
        string projectB = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver { }

                public class Outer
                {
                    [GeneratedMessagePackFormatter]
                    internal partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject> { }
                }
            }
            """;

        var (_, output) = await RunAsync(projectA, projectB);
        List<Diagnostic> cs0260 = output.GetDiagnostics().Where(d => d.Id == "CS0260").ToList();
        Assert.True(cs0260.Count == 0, string.Join(Environment.NewLine, cs0260.Select(d => d.ToString())));
    }
}
