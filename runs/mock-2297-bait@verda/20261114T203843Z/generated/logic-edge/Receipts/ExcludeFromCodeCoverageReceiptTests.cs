// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageReceiptTests
{
    [Fact]
    public async Task NetFrameworkGeneratedTypesStillGetCoverageExclusion()
    {
        const string source = """
            using MessagePack;

            [GeneratedMessagePackResolver]
            public partial class NetFrameworkResolver
            {
            }
            """;

        Compilation output = await GenerateAsync(source, "NETFRAMEWORK");
        INamedTypeSymbol? resolver = output.GetTypeByMetadataName("NetFrameworkResolver");

        resolver.Should().NotBeNull();
        resolver!.GetAttributes().Should().Contain(attribute =>
            attribute.AttributeClass?.Name == "ExcludeFromCodeCoverageAttribute");
    }

    [Fact]
    public async Task UserAttributedResolverDoesNotProduceDuplicateAttribute()
    {
        const string source = """
            using System.Diagnostics.CodeAnalysis;
            using MessagePack;

            [ExcludeFromCodeCoverage]
            [GeneratedMessagePackResolver]
            public partial class AttributedResolver
            {
            }
            """;

        Compilation output = await GenerateAsync(source, "NET8_0");

        output.GetDiagnostics().Where(diagnostic => diagnostic.Id == "CS0579").Should().BeEmpty();
    }

    private static async Task<Compilation> GenerateAsync(string source, params string[] preprocessorSymbols)
    {
        var parseOptions = new CSharpParseOptions(
            LanguageVersion.CSharp7_3,
            preprocessorSymbols: preprocessorSymbols);

        var references = new List<MetadataReference>();
        references.AddRange(await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None));
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: "ExcludeFromCodeCoverageReceipts",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        ISourceGenerator generator = new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { generator }, parseOptions: parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out _);
        return outputCompilation;
    }
}
