// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class EnumExcludeFromCodeCoverageRequirementTests
{
    private const string AttributeName = "global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage";

    [Fact]
    public async Task EnumFormatter_HasActiveExcludeFromCodeCoverageAttributeOnModernTargets()
    {
        const string source = """
            using MessagePack;

            public enum MyEnum
            {
                None,
                Value,
            }

            [MessagePackObject]
            public class EnumHolder
            {
                [Key(0)]
                public MyEnum Value { get; set; }
            }

            [GeneratedMessagePackResolver]
            partial class TestResolver
            {
            }
            """;

        MetadataReference[] references = (await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None))
            .AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack))
            .ToArray();
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Receipts.GeneratedTests.Enum",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        GeneratorDriverRunResult result = driver.GetRunResult();
        string formatter = result.GeneratedTrees
            .Select(tree => tree.ToString())
            .Single(text => text.Contains("IMessagePackFormatter<global::MyEnum>", StringComparison.Ordinal));

        int activeAttributes = CSharpSyntaxTree.ParseText(formatter, new CSharpParseOptions(LanguageVersion.Latest))
            .GetRoot()
            .DescendantNodes()
            .OfType<AttributeSyntax>()
            .Count(attribute => attribute.Name.ToString() == AttributeName);
        Assert.True(activeAttributes > 0, formatter);
    }
}
