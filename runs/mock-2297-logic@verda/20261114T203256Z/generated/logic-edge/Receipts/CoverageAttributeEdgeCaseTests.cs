// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class CoverageAttributeEdgeCaseTests
{
    [Fact]
    public async Task EnumFormatter_IsExcludedFromCodeCoverage_OnModernTargets()
    {
        const string source = """
            using MessagePack;

            public enum SampleEnum
            {
                None,
                Value,
            }

            [MessagePackObject]
            public class SampleObject
            {
                [Key(0)]
                public SampleEnum Value { get; set; }
            }

            [GeneratedMessagePackResolver]
            public partial class SampleResolver
            {
            }
            """;

        IEnumerable<MetadataReference> references =
            (await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None))
            .Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "CoverageAttributeReceipt",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out _);

        ClassDeclarationSyntax enumFormatter = outputCompilation.SyntaxTrees
            .SelectMany(tree => tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Single(type => type.Identifier.ValueText.EndsWith("SampleEnumFormatter", StringComparison.Ordinal));
        bool hasCoverageAttribute = enumFormatter.AttributeLists
            .SelectMany(list => list.Attributes)
            .Any(attribute => attribute.Name.ToString().EndsWith("ExcludeFromCodeCoverage", StringComparison.Ordinal));

        hasCoverageAttribute.Should().BeTrue("enum formatters on modern targets should be excluded from code coverage");
    }

    [Fact]
    public async Task ResolverWithUserSuppliedCoverageAttributeStillCompiles()
    {
        const string source = """
            using MessagePack;

            [GeneratedMessagePackResolver]
            [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
            public partial class ExistingCoverageAttributeResolver
            {
            }
            """;
        var test = new VerifyCS.Test(ReferencesSet.MessagePack)
        {
            TestState = { Sources = { source } },
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
        };

        await test.RunAsync();
    }
}
