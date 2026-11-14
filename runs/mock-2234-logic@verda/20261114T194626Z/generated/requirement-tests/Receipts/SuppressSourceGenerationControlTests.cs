// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class SuppressSourceGenerationControlTests
{
    [Fact]
    public async Task StringKeyedContractStillGeneratesMembersWhenNotSuppressed()
    {
        const string source = """
            using MessagePack;

            [MessagePackObject]
            public partial class MyObject
            {
                [Key("name")]
                public string Name { get; set; } = "";

                [Key("age")]
                public int Age { get; set; }
            }
            """;

        var references = (await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None))
            .AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        var compilation = CSharpCompilation.Create(
            "UnsuppressedControlReceipt",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp7_3)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        string generated = string.Join(
            "\n",
            driver.GetRunResult().Results.SelectMany(result => result.GeneratedSources).Select(generatedSource => generatedSource.SourceText.ToString()));
        generated.Should().Contain("\"name\"", "behavior with source generation enabled must not change");
        generated.Should().Contain("\"age\"", "behavior with source generation enabled must not change");
    }
}
