// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class DuplicateAndMixedKeyGeneratorReceipts
{
    private static async Task<List<string>> RunGeneratorAsync(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3);
        var framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        IEnumerable<MetadataReference> refs = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        var compilation = CSharpCompilation.Create(
            "ReceiptsTest",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGenerators(compilation);
        var result = driver.GetRunResult();
        return result.Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()).ToList();
    }

    private static void AssertNoSilentDrop(List<string> sources)
    {
        foreach (var s in sources.Where(s => s.Contains("Alpha")))
        {
            Assert.True(s.Contains("Bravo"), "Generated formatter serializes Alpha but silently drops Bravo:\n" + s);
        }
    }

    [Fact]
    public async Task DuplicateIntKey_GeneratedFormatterDoesNotSilentlyDropMember()
    {
        string source = """
            using MessagePack;

            namespace Foo
            {
                [MessagePackObject]
                public class MyObject
                {
                    [Key(0)]
                    public int Alpha { get; set; }

                    [Key(0)]
                    public int Bravo { get; set; }
                }

                [GeneratedMessagePackResolver]
                partial class MyResolver { }
            }
            """;
        var sources = await RunGeneratorAsync(source);
        AssertNoSilentDrop(sources);
    }

    [Fact]
    public async Task MixedKeys_GeneratedFormatterDoesNotSilentlyDropMember()
    {
        string source = """
            using MessagePack;

            namespace Foo
            {
                [MessagePackObject]
                public class MyObject
                {
                    [Key(0)]
                    public int Alpha { get; set; }

                    [Key("b")]
                    public int Bravo { get; set; }
                }

                [GeneratedMessagePackResolver]
                partial class MyResolver { }
            }
            """;
        var sources = await RunGeneratorAsync(source);
        AssertNoSilentDrop(sources);
    }
}
