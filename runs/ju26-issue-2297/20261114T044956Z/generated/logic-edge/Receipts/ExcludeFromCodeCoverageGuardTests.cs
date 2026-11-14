// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageGuardTests
{
    private const string Source = """
        using MessagePack;

        namespace Foo
        {
            [MessagePackObject]
            public class MyObject
            {
                [Key(0)]
                public int A { get; set; }
            }

            [GeneratedMessagePackResolver]
            partial class MyResolver { }
        }
        """;

    [Theory]
    [InlineData("NETSTANDARD2_0")]
    [InlineData("NETFRAMEWORK")]
    public async Task GeneratedFormattersAreExcludedFromCoverage(string symbol)
    {
        ImmutableArray<MetadataReference> frameworkRefs = symbol == "NETSTANDARD2_0"
            ? await ReferenceAssemblies.NetStandard.NetStandard20.ResolveAsync(LanguageNames.CSharp, CancellationToken.None)
            : await ReferenceAssemblies.NetFramework.Net472.Default.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);

        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: new[] { symbol });
        SyntaxTree tree = CSharpSyntaxTree.ParseText(Source, parseOptions);
        List<MetadataReference> references = frameworkRefs.ToList();
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // The attribute exists on this target framework, so there is no reason to omit it.
        compilation.GetTypeByMetadataName("System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute").Should().NotBeNull();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator()).WithUpdatedParseOptions(parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> _);

        HashSet<SyntaxTree> originalTrees = new HashSet<SyntaxTree>(compilation.SyntaxTrees);
        List<string> formatters = new List<string>();
        List<string> missing = new List<string>();
        foreach (SyntaxTree generated in output.SyntaxTrees.Where(t => !originalTrees.Contains(t)))
        {
            SemanticModel model = output.GetSemanticModel(generated);
            foreach (ClassDeclarationSyntax cds in generated.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (!cds.Identifier.ValueText.EndsWith("Formatter", StringComparison.Ordinal))
                {
                    continue;
                }

                INamedTypeSymbol? type = model.GetDeclaredSymbol(cds);
                if (type is null)
                {
                    continue;
                }

                formatters.Add(type.ToDisplayString());
                if (!type.GetAttributes().Any(a => a.AttributeClass?.Name == "ExcludeFromCodeCoverageAttribute"))
                {
                    missing.Add(type.ToDisplayString());
                }
            }
        }

        formatters.Should().NotBeEmpty();
        missing.Should().BeEmpty();
    }
}
