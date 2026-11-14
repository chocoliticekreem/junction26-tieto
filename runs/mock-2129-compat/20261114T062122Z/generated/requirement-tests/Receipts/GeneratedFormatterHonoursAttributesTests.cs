// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterHonoursAttributesTests
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp12);

    private const string ProjectBSource = """
        using MessagePack;
        using MessagePack.Formatters;

        namespace ProjectB
        {
            [GeneratedMessagePackFormatter]
            public partial class TaggedFormatter : IMessagePackFormatter<ProjectA.Tagged>
            {
            }

            [GeneratedMessagePackResolver]
            partial class MyResolver { }
        }
        """;

    private static async Task<List<MetadataReference>> GetReferencesAsync()
    {
        List<MetadataReference> references = new();
        references.AddRange(await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None));
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        return references;
    }

    private static async Task<string> GenerateFormatterSourceAsync(string projectASource)
    {
        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(projectASource, ParseOptions) },
            await GetReferencesAsync(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        var emitResult = projectA.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        List<MetadataReference> references = await GetReferencesAsync();
        references.Add(MetadataReference.CreateFromImage(stream.ToArray()));
        CSharpCompilation projectB = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(ProjectBSource, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(projectB, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);

        var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));

        var formatterTree = driver.GetRunResult().GeneratedTrees.FirstOrDefault(t => t.ToString().Contains("TaggedFormatter"));
        Assert.NotNull(formatterTree);
        return formatterTree!.ToString();
    }

    [Fact]
    public async Task MessagePackObjectIgnoreMemberIsHonoured()
    {
        string generated = await GenerateFormatterSourceAsync("""
            using MessagePack;

            namespace ProjectA
            {
                [MessagePackObject]
                public class Tagged
                {
                    [Key(0)]
                    public int Id { get; set; }

                    [IgnoreMember]
                    public int Secret { get; set; }
                }
            }
            """);

        Assert.DoesNotContain("Secret", generated);
    }

    [Fact]
    public async Task DataContractIgnoreDataMemberIsHonoured()
    {
        string generated = await GenerateFormatterSourceAsync("""
            using System.Runtime.Serialization;

            namespace ProjectA
            {
                [DataContract]
                public class Tagged
                {
                    [DataMember(Order = 0)]
                    public int Id { get; set; }

                    [IgnoreDataMember]
                    public int Secret { get; set; }
                }
            }
            """);

        Assert.DoesNotContain("Secret", generated);
    }
}
