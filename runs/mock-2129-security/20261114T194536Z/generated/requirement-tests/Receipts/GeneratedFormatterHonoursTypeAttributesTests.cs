// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterHonoursTypeAttributesTests
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    private const string ProjectBSource = """
        using MessagePack;
        using MessagePack.Formatters;

        namespace ProjectB
        {
            [GeneratedMessagePackFormatter]
            public partial class MyTypeFormatter : IMessagePackFormatter<ProjectA.MyType>;

            [GeneratedMessagePackResolver]
            partial class MyResolver
            {
            }
        }
        """;

    [Fact]
    public async Task IgnoreMemberOnReferencedType_IsNotSerialized()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        string projectASource = """
            using MessagePack;

            namespace ProjectA
            {
                [MessagePackObject]
                public class MyType
                {
                    [Key(0)]
                    public int KeptValue { get; set; }

                    [IgnoreMember]
                    public int IgnoredSecretValue { get; set; }
                }
            }
            """;
        MetadataReference projectA = EmitLibrary("ProjectA", projectASource, MessagePackAndFramework(framework));

        GeneratorDriverRunResult run = RunGenerator(MessagePackAndFramework(framework).Append(projectA));

        string formatterSource = GetFormatterSource(run, "MyTypeFormatter");
        Assert.Contains("KeptValue", formatterSource, StringComparison.Ordinal);
        Assert.DoesNotContain("IgnoredSecretValue", formatterSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DataContractOnReferencedType_LimitsMembers()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        string projectASource = """
            using System.Runtime.Serialization;

            namespace ProjectA
            {
                [DataContract]
                public class MyType
                {
                    [DataMember(Order = 0)]
                    public int KeptValue { get; set; }

                    public int NotADataMember { get; set; }
                }
            }
            """;
        MetadataReference projectA = EmitLibrary("ProjectA", projectASource, framework);

        GeneratorDriverRunResult run = RunGenerator(MessagePackAndFramework(framework).Append(projectA));

        string formatterSource = GetFormatterSource(run, "MyTypeFormatter");
        Assert.Contains("KeptValue", formatterSource, StringComparison.Ordinal);
        Assert.DoesNotContain("NotADataMember", formatterSource, StringComparison.Ordinal);
    }

    private static List<MetadataReference> MessagePackAndFramework(ImmutableArray<MetadataReference> framework)
    {
        return framework
            .Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack).Cast<MetadataReference>())
            .ToList();
    }

    private static MetadataReference EmitLibrary(string name, string source, IEnumerable<MetadataReference> references)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            name,
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray().ToImmutableArray());
    }

    private static GeneratorDriverRunResult RunGenerator(IEnumerable<MetadataReference> references)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(ProjectBSource, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new ISourceGenerator[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    private static string GetFormatterSource(GeneratorDriverRunResult run, string formatterName)
    {
        SyntaxTree? tree = run.GeneratedTrees.FirstOrDefault(t => t.ToString().Contains("partial class " + formatterName, StringComparison.Ordinal));
        Assert.True(tree is not null, "No generated source declares partial class " + formatterName);
        return tree!.ToString();
    }
}
