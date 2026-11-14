// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;
using System.Threading.Tasks;
using MessagePack;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class Req2297ReceiptTests
{
    private const string ResolverName = "ExcludeFromCodeCoverageFrameworkResolver";

    [Theory]
    [InlineData("NETFRAMEWORK")]
    [InlineData("NETSTANDARD2_0")]
    public async Task ResolverRetainsExcludeFromCodeCoverageOnFrameworksWhereItExists(string targetFrameworkSymbol)
    {
        string source = $$"""
            using MessagePack;

            [GeneratedMessagePackResolver]
            public partial class {{ResolverName}}
            {
            }

            [MessagePackObject]
            public class CoveredType
            {
                [Key(0)]
                public int Value { get; set; }
            }
            """;

        var parseOptions = new CSharpParseOptions(
            LanguageVersion.Latest,
            preprocessorSymbols: new[] { targetFrameworkSymbol });
        ImmutableArray<MetadataReference> references = await ReferenceAssemblies.Net.Net80.ResolveAsync(
            LanguageNames.CSharp,
            CancellationToken.None);
        references = references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "Req2297ReceiptCompilation",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation generatedCompilation,
            out _);

        INamedTypeSymbol? resolver = generatedCompilation.GetTypeByMetadataName(ResolverName);
        Assert.NotNull(resolver);
        Assert.Contains(
            resolver!.GetAttributes(),
            attribute => attribute.AttributeClass?.Name == "ExcludeFromCodeCoverageAttribute");
    }

    [Fact]
    public void CopyingUntrustedSecurityRetainsHashCollisionResistance()
    {
        var copiedSecurity = new MessagePackSecurity(MessagePackSecurity.UntrustedData);

        Assert.True(copiedSecurity.HashCollisionResistant);
    }
}
