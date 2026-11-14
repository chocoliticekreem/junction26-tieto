// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
    public async Task IgnoreMemberOnReferencedMessagePackObjectIsHonoured()
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
                    public int IgnoredSecretValue { get; set; }
                }
            }
            """;

        string formatter = await GenerateFormatterSourceAsync(projectA, projectAReferencesMessagePack: true);
        Assert.Contains("Id", formatter);
        Assert.DoesNotContain("IgnoredSecretValue", formatter);
    }

    [Fact]
    public async Task IgnoreDataMemberOnReferencedDataContractIsHonoured()
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

                    [IgnoreDataMember]
                    public int IgnoredSecretValue { get; set; }
                }
            }
            """;

        string formatter = await GenerateFormatterSourceAsync(projectA, projectAReferencesMessagePack: false);
        Assert.Contains("Id", formatter);
        Assert.DoesNotContain("IgnoredSecretValue", formatter);
    }

    private static async Task<string> GenerateFormatterSourceAsync(string projectASource, bool projectAReferencesMessagePack)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> messagePack = Enumerable.Concat<MetadataReference>(Array.Empty<MetadataReference>(), ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        CSharpParseOptions parseOptions = new(LanguageVersion.CSharp12);

        List<MetadataReference> projectARefs = new(framework);
        if (projectAReferencesMessagePack)
        {
            projectARefs.AddRange(messagePack);
        }

        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(projectASource, parseOptions) },
            projectARefs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        var emitResult = projectA.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics.Select(d => d.ToString())));

        List<MetadataReference> references = new(framework);
        references.AddRange(messagePack);
        references.Add(MetadataReference.CreateFromImage(stream.ToArray()));

        CSharpCompilation projectB = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(ProjectBSource, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(projectB, out Compilation _, out ImmutableArray<Diagnostic> _);
        GeneratorDriverRunResult run = driver.GetRunResult();

        Assert.All(run.Results, r => Assert.Null(r.Exception));
        string[] sources = run.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => s.SourceText.ToString())
            .Where(t => t.Contains("class MyTypeFormatter"))
            .ToArray();
        Assert.True(sources.Length > 0, "No generated source declares class MyTypeFormatter.");
        return string.Join(Environment.NewLine, sources);
    }
}
