// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterHonoursAttributesTests
{
    [Fact]
    public async Task IgnoreMemberOnFormattedTypeIsHonoured()
    {
        string source = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace TestNs
            {
                [MessagePackObject]
                public class MyObject
                {
                    [Key(0)]
                    public int Kept { get; set; }

                    [IgnoreMember]
                    public int IgnoredSecret { get; set; }
                }

                [GeneratedMessagePackFormatter]
                public partial class UserObjectFormatter : IMessagePackFormatter<MyObject>
                {
                }

                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }
            }
            """;

        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = new(framework);
        foreach (MetadataReference r in ReferencesHelper.GetReferences(ReferencesSet.MessagePack))
        {
            references.Add(r);
        }

        CSharpParseOptions parseOptions = new(LanguageVersion.Latest);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePackGenerator());
        driver = driver.RunGenerators(compilation);
        GeneratorDriverRunResult result = driver.GetRunResult();

        List<string> userFormatterSources = result.GeneratedTrees
            .Select(t => t.ToString())
            .Where(t => t.Contains("class UserObjectFormatter"))
            .ToList();

        Assert.NotEmpty(userFormatterSources);
        Assert.DoesNotContain(userFormatterSources, t => t.Contains("IgnoredSecret"));
    }
}
