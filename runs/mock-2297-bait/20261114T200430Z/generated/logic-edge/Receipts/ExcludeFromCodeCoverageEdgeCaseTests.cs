// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageEdgeCaseTests
{
    private const string ResolverSource = """
        [MessagePack.GeneratedMessagePackResolver]
        [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
        partial class MyResolver
        {
        }

        [MessagePack.MessagePackObject]
        public class MyObject
        {
            [MessagePack.Key(0)]
            public int Value { get; set; }
        }
        """;

    private const string PlainResolverSource = """
        [MessagePack.GeneratedMessagePackResolver]
        partial class MyResolver
        {
        }

        [MessagePack.MessagePackObject]
        public class MyObject
        {
            [MessagePack.Key(0)]
            public int Value { get; set; }
        }
        """;

    [Fact]
    public async Task UserResolverAlreadyExcludedFromCoverage_StillCompiles()
    {
        var test = new VerifyCS.Test
        {
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
        };
        test.TestState.Sources.Add(ResolverSource);
        await test.RunAsync();
    }

    [Theory]
    [InlineData("NETSTANDARD2_0")]
    [InlineData("NETFRAMEWORK")]
    public async Task GeneratedResolverIsExcludedFromCoverage_WhenTargetDefinesSymbol(string symbol)
    {
        var frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3, preprocessorSymbols: new[] { symbol });
        var references = frameworkRefs.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        var compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(PlainResolverSource, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);

        INamedTypeSymbol? resolver = output.GetTypeByMetadataName("MyResolver");
        Assert.NotNull(resolver);
        bool hasAttribute = resolver!.GetAttributes().Any(a => a.AttributeClass?.Name == "ExcludeFromCodeCoverageAttribute");
        Assert.True(hasAttribute, $"Generated resolver lacks ExcludeFromCodeCoverage when {symbol} is defined, although the attribute exists on that target.");
    }
}
