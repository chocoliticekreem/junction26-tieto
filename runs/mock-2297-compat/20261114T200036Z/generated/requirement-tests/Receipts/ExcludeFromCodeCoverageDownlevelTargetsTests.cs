// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageDownlevelTargetsTests
{
    private const string ExcludeAttributeName = "System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute";

    private const string Source = """
        using MessagePack;

        namespace TestNs
        {
            [GeneratedMessagePackResolver]
            partial class MyResolver
            {
            }

            [MessagePackObject]
            public class IntKeyed
            {
                [Key(0)]
                public int A { get; set; }
            }
        }
        """;

    [Theory]
    [InlineData("NETSTANDARD2_0")]
    [InlineData("NETFRAMEWORK")]
    public async Task GeneratedFormattersAreDecoratedWhenTargetingDownlevelFrameworks(string preprocessorSymbol)
    {
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: new[] { preprocessorSymbol });
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = new List<MetadataReference>(framework);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsExcludeFromCodeCoverageDownlevel",
            new[] { CSharpSyntaxTree.ParseText(Source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator()).WithUpdatedParseOptions(parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> _);
        List<SyntaxTree> generated = output.SyntaxTrees.Where(t => !compilation.SyntaxTrees.Contains(t)).ToList();
        Assert.NotEmpty(generated);

        int formatterCount = 0;
        foreach (SyntaxTree tree in generated)
        {
            SemanticModel model = output.GetSemanticModel(tree);
            foreach (ClassDeclarationSyntax decl in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (!decl.Identifier.ValueText.EndsWith("Formatter", StringComparison.Ordinal))
                {
                    continue;
                }

                INamedTypeSymbol? symbol = model.GetDeclaredSymbol(decl);
                Assert.NotNull(symbol);
                formatterCount++;
                bool decorated = symbol!.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == ExcludeAttributeName);
                Assert.True(decorated, $"Generated formatter {symbol.Name} is not decorated with ExcludeFromCodeCoverage when {preprocessorSymbol} is defined.");
            }
        }

        Assert.True(formatterCount > 0, "No generated formatter was found.");
    }
}
