// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageOnDownlevelTargetsTests
{
    private const string AttributeName = "System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute";

    private const string Source = """
        using MessagePack;

        namespace TestNs
        {
            [MessagePackObject]
            public class IntKeyed
            {
                [Key(0)]
                public int A { get; set; }
            }

            [MessagePackObject(true)]
            public class StringKeyed
            {
                public int B { get; set; }
            }

            [GeneratedMessagePackResolver]
            public partial class MyResolver { }
        }
        """;

    // ExcludeFromCodeCoverageAttribute exists in netstandard2.0 and .NET Framework 4.0+,
    // so generated code compiled for those targets must still be decorated.
    [Theory]
    [InlineData("NETSTANDARD2_0")]
    [InlineData("NETFRAMEWORK")]
    public async Task GeneratedTypesAreExcludedWhenTargetSymbolDefined(string symbol)
    {
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: new[] { symbol });
        ImmutableArray<MetadataReference> frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = Enumerable.Concat<MetadataReference>(frameworkRefs, ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsCoverageDownlevelTest",
            new[] { CSharpSyntaxTree.ParseText(Source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        Assert.DoesNotContain(generatorDiagnostics, d => d.Severity == DiagnosticSeverity.Error);

        List<SyntaxTree> generatedTrees = output.SyntaxTrees.Where(t => !compilation.SyntaxTrees.Contains(t)).ToList();
        Assert.NotEmpty(generatedTrees);

        HashSet<INamedTypeSymbol> seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        List<string> missing = new List<string>();
        foreach (SyntaxTree tree in generatedTrees)
        {
            SemanticModel model = output.GetSemanticModel(tree);
            foreach (TypeDeclarationSyntax decl in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(decl) is INamedTypeSymbol type
                    && type.GetMembers().Any(m => !m.IsImplicitlyDeclared && m is not INamedTypeSymbol)
                    && seen.Add(type)
                    && !type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == AttributeName))
                {
                    missing.Add(type.ToDisplayString());
                }
            }
        }

        Assert.NotEmpty(seen);
        Assert.Empty(missing);
    }
}
