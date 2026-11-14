// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterHonoursTypeAttributesTests
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp12);

    private const string ProjectBSource = """
        using MessagePack;
        using MessagePack.Formatters;

        namespace ProjectB;

        [GeneratedMessagePackFormatter]
        partial class MyTypeFormatter : IMessagePackFormatter<ProjectA.MyType>;

        [GeneratedMessagePackResolver]
        partial class MyResolver
        {
        }
        """;

    [Fact]
    public async Task DataContractOnReferencedType_IsHonoured()
    {
        string projectASource = """
            using System.Runtime.Serialization;

            namespace ProjectA
            {
                [DataContract]
                public class MyType
                {
                    [DataMember(Name = "id")]
                    public int Id { get; set; }

                    public string Secret { get; set; } = "";
                }
            }
            """;
        MetadataReference projectA = await CompileProjectAAsync(projectASource, referenceMessagePack: false);
        string formatterSource = await GenerateFormatterSourceAsync(projectA);

        Assert.False(string.IsNullOrEmpty(formatterSource), "No formatter body was generated for MyTypeFormatter.");
        Assert.DoesNotContain("Secret", formatterSource);
    }

    [Fact]
    public async Task IgnoreMemberOnReferencedType_IsHonoured()
    {
        string projectASource = """
            using MessagePack;

            namespace ProjectA
            {
                [MessagePackObject(keyAsPropertyName: true)]
                public class MyType
                {
                    public int Id { get; set; }

                    [IgnoreMember]
                    public string Secret { get; set; } = "";
                }
            }
            """;
        MetadataReference projectA = await CompileProjectAAsync(projectASource, referenceMessagePack: true);
        string formatterSource = await GenerateFormatterSourceAsync(projectA);

        Assert.False(string.IsNullOrEmpty(formatterSource), "No formatter body was generated for MyTypeFormatter.");
        Assert.DoesNotContain("Secret", formatterSource);
    }

    private static async Task<IEnumerable<MetadataReference>> GetReferencesAsync(bool referenceMessagePack)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        return referenceMessagePack
            ? Enumerable.Concat<MetadataReference>(framework, ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList()
            : framework.ToList();
    }

    private static async Task<MetadataReference> CompileProjectAAsync(string source, bool referenceMessagePack)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            await GetReferencesAsync(referenceMessagePack),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.ToString())));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static async Task<string> GenerateFormatterSourceAsync(MetadataReference projectA)
    {
        List<MetadataReference> references = (await GetReferencesAsync(referenceMessagePack: true)).ToList();
        references.Add(projectA);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(ProjectBSource, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation _, out ImmutableArray<Diagnostic> _);
        return string.Join("\n", driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()).Where(s => s.Contains("partial class MyTypeFormatter")));
    }
}
