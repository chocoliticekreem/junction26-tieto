// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterLogicReceiptsTests
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    private const string PlainProjectA = """
        namespace ProjectA
        {
            public class MyType
            {
                public int Id { get; set; }

                public string Name { get; set; }
            }
        }
        """;

    [Fact]
    public async Task AttributesOnFormattedTypeAreHonoured()
    {
        ImmutableArray<MetadataReference> references = await GetReferencesAsync();
        MetadataReference projectA = CompileProjectA("""
            using MessagePack;
            namespace ProjectA
            {
                [MessagePackObject]
                public class MyType
                {
                    [Key(0)]
                    public int Id { get; set; }

                    [IgnoreMember]
                    public string Secret { get; set; }
                }
            }
            """, references);

        GeneratorDriverRunResult result = RunGenerator("""
            using MessagePack;
            using MessagePack.Formatters;
            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                public partial class MyTypeFormatter : IMessagePackFormatter<ProjectA.MyType> { }

                [GeneratedMessagePackResolver]
                partial class MyResolver { }
            }
            """, references.Add(projectA));

        List<string> formatterSources = GetFormatterSources(result);
        Assert.NotEmpty(formatterSources);
        Assert.DoesNotContain(formatterSources, s => s.Contains("Secret"));
    }

    [Fact]
    public async Task SurrogateWithPrivateFieldIsAccepted()
    {
        ImmutableArray<MetadataReference> references = await GetReferencesAsync();
        MetadataReference projectA = CompileProjectA(PlainProjectA, references);

        GeneratorDriverRunResult result = RunGenerator("""
            using MessagePack;
            using MessagePack.Formatters;
            namespace ProjectB
            {
                [MessagePackObject]
                public class MyTypeSurrogate
                {
                    private int cache = 1;

                    [Key(0)]
                    public int Id { get; set; }

                    [Key(1)]
                    public string Name { get; set; }

                    public int GetCache() => cache;
                }

                [GeneratedMessagePackFormatter(SurrogateType = typeof(MyTypeSurrogate))]
                public partial class MyTypeFormatter : IMessagePackFormatter<ProjectA.MyType> { }

                [GeneratedMessagePackResolver]
                partial class MyResolver { }
            }
            """, references.Add(projectA));

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task KeyAsPropertyNameSurrogateIsAccepted()
    {
        ImmutableArray<MetadataReference> references = await GetReferencesAsync();
        MetadataReference projectA = CompileProjectA(PlainProjectA, references);

        GeneratorDriverRunResult result = RunGenerator("""
            using MessagePack;
            using MessagePack.Formatters;
            namespace ProjectB
            {
                [MessagePackObject(true)]
                public class MyTypeSurrogate
                {
                    public int Id { get; set; }

                    public string Name { get; set; }
                }

                [GeneratedMessagePackFormatter(SurrogateType = typeof(MyTypeSurrogate))]
                public partial class MyTypeFormatter : IMessagePackFormatter<ProjectA.MyType> { }

                [GeneratedMessagePackResolver]
                partial class MyResolver { }
            }
            """, references.Add(projectA));

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task FormatterIsGeneratedWithoutResolverDeclaration()
    {
        ImmutableArray<MetadataReference> references = await GetReferencesAsync();
        MetadataReference projectA = CompileProjectA(PlainProjectA, references);

        GeneratorDriverRunResult result = RunGenerator("""
            using MessagePack;
            using MessagePack.Formatters;
            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                public partial class MyTypeFormatter : IMessagePackFormatter<ProjectA.MyType> { }
            }
            """, references.Add(projectA));

        Assert.NotEmpty(GetFormatterSources(result));
    }

    private static async Task<ImmutableArray<MetadataReference>> GetReferencesAsync()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        return framework.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
    }

    private static MetadataReference CompileProjectA(string source, ImmutableArray<MetadataReference> references)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        var emitResult = compilation.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static GeneratorDriverRunResult RunGenerator(string source, IEnumerable<MetadataReference> references)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new MessagePackGenerator().AsSourceGenerator() }, parseOptions: ParseOptions);
        driver = driver.RunGenerators(compilation);
        return driver.GetRunResult();
    }

    private static List<string> GetFormatterSources(GeneratorDriverRunResult result)
    {
        return result.GeneratedTrees
            .Select(t => t.ToString())
            .Where(s => s.Contains("class MyTypeFormatter"))
            .ToList();
    }
}
