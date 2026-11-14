// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using System.Threading;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class StringKeyedFieldCollectionReceiptTests
{
    [Fact]
    public async Task StringKeyedFields_AreRetainedInGeneratedFormatter()
    {
        const string source = """
            using MessagePack;

            [MessagePackObject]
            public class StringKeyedFieldObject
            {
                [Key("alpha")]
                public int StringKeyedFieldAlpha;

                [Key("beta")]
                public int StringKeyedFieldBeta;
            }

            [GeneratedMessagePackResolver]
            public partial class TestResolver
            {
            }
            """;

        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var references = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        references = references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        var compilation = CSharpCompilation.Create(
            "StringKeyedFieldReceipt",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        var generatedSources = string.Join(
            "\n",
            driver.GetRunResult().Results
                .SelectMany(result => result.GeneratedSources)
                .Select(generatedSource => generatedSource.SourceText.ToString()));

        generatedSources.Should().Contain("StringKeyedFieldAlpha");
        generatedSources.Should().Contain("StringKeyedFieldBeta");
    }
}
