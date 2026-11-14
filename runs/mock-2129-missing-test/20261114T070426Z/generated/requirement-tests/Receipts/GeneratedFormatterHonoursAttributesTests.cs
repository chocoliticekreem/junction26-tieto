// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterHonoursAttributesTests
{
    private static async Task<ImmutableArray<MetadataReference>> GetReferencesAsync()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        return framework.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
    }

    private static MetadataReference BuildProjectA(string source, IEnumerable<MetadataReference> references)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        var emitResult = compilation.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static async Task<string> GenerateFormatterTextAsync(string projectASource, string projectBSource, string formatterName)
    {
        ImmutableArray<MetadataReference> references = await GetReferencesAsync();
        MetadataReference projectA = BuildProjectA(projectASource, references);
        CSharpParseOptions parseOptions = new(LanguageVersion.CSharp12);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(projectBSource, parseOptions) },
            references.Add(projectA),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
        List<Diagnostic> errors = diagnostics.Concat(output.GetDiagnostics()).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        string[] texts = driver.GetRunResult().GeneratedTrees
            .Select(t => t.ToString())
            .Where(t => t.Contains("partial class " + formatterName))
            .ToArray();
        Assert.NotEmpty(texts);
        return string.Join(Environment.NewLine, texts);
    }

    [Fact]
    public async Task IgnoreMemberOnReferencedType_IsNotSerialized()
    {
        string projectA = """
            using MessagePack;

            namespace ProjectA
            {
                [MessagePackObject]
                public class Tagged
                {
                    [Key(0)]
                    public int Id { get; set; }

                    [IgnoreMember]
                    public string SecretValue { get; set; }
                }
            }
            """;
        string projectB = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                partial class TaggedFormatter : IMessagePackFormatter<ProjectA.Tagged>
                {
                }

                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }
            }
            """;
        string text = await GenerateFormatterTextAsync(projectA, projectB, "TaggedFormatter");
        Assert.DoesNotContain("SecretValue", text);
    }

    [Fact]
    public async Task DataContractOnReferencedType_LimitsMembers()
    {
        string projectA = """
            using System.Runtime.Serialization;

            namespace ProjectA
            {
                [DataContract]
                public class Contract
                {
                    [DataMember(Order = 0)]
                    public int Id { get; set; }

                    public string Unmarked { get; set; }
                }
            }
            """;
        string projectB = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                partial class ContractFormatter : IMessagePackFormatter<ProjectA.Contract>
                {
                }

                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }
            }
            """;
        string text = await GenerateFormatterTextAsync(projectA, projectB, "ContractFormatter");
        Assert.DoesNotContain("Unmarked", text);
    }
}
