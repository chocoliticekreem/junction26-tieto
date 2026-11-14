// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageEdgeCaseTests
{
    private const string ResolverSource = """
        using MessagePack;

        namespace Test
        {
            [MessagePackObject]
            public class MyObject
            {
                [Key(0)]
                public int Value { get; set; }
            }

            [GeneratedMessagePackResolver]
            partial class MyResolver
            {
            }
        }
        """;

    [Fact]
    public async Task UserResolverAlreadyExcludedFromCoverage_CompilesWithoutDuplicateAttribute()
    {
        string testSource = """
            using MessagePack;

            namespace Test
            {
                [MessagePackObject]
                public class MyObject
                {
                    [Key(0)]
                    public int Value { get; set; }
                }

                [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }
            }
            """;

        var test = new VerifyCS.Test
        {
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
        };
        test.TestState.Sources.Add(testSource);
        await test.RunAsync();
    }

    [Theory]
    [InlineData("NETSTANDARD2_0")]
    [InlineData("NETFRAMEWORK")]
    public async Task GeneratedResolverIsExcludedFromCoverageOnNetStandardAndNetFramework(string symbol)
    {
        var parseOptions = new CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest, preprocessorSymbols: new[] { symbol });
        var frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var refs = new List<MetadataReference>(frameworkRefs);
        refs.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        var compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(ResolverSource, parseOptions) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);

        INamedTypeSymbol? resolver = output.GetTypeByMetadataName("Test.MyResolver");
        resolver.Should().NotBeNull();
        resolver!.GetAttributes().Select(a => a.AttributeClass?.Name).Should().Contain("ExcludeFromCodeCoverageAttribute");
    }
}
