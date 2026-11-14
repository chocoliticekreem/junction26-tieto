// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class StringKeyedMembersStillGeneratedTests
{
    [Fact]
    public async Task StringKeyedFieldAndPropertyAppearInGeneratedFormatter()
    {
        string source = """
            using MessagePack;

            namespace TestNs
            {
                [MessagePackObject]
                public class MyObject
                {
                    [Key("propKey")]
                    public int PropMember { get; set; }

                    [Key("fieldKey")]
                    public int FieldMember;
                }

                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }
            }
            """;

        ImmutableArray<MetadataReference> frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = new List<MetadataReference>(frameworkRefs);
        foreach (MetadataReference r in ReferencesHelper.GetReferences(ReferencesSet.MessagePack))
        {
            references.Add(r);
        }

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsGenAssembly",
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator());
        driver = driver.RunGenerators(compilation);
        GeneratorDriverRunResult runResult = driver.GetRunResult();

        Assert.All(runResult.Results, r => Assert.Null(r.Exception));
        string generated = string.Join("\n", runResult.GeneratedTrees.Select(t => t.ToString()));
        Assert.Contains("PropMember", generated);
        Assert.Contains("FieldMember", generated);
    }
}
