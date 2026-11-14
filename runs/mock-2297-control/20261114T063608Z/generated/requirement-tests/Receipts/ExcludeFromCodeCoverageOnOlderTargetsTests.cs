// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageOnOlderTargetsTests
{
    private const string AttributeName = "System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute";

    private const string Source = """
        using MessagePack;

        namespace MyNs
        {
            [GeneratedMessagePackResolver]
            partial class MyResolver { }

            [MessagePackObject]
            public class IntKeyed { [Key(0)] public int A { get; set; } }
        }
        """;

    // ExcludeFromCodeCoverageAttribute exists in netstandard2.0 and .NET Framework 4.0+,
    // so generated code built for those targets should be excluded from coverage too.
    [Theory]
    [InlineData("NETSTANDARD2_0")]
    [InlineData("NETFRAMEWORK")]
    public async Task GeneratedTypesAreExcludedOnTargetsThatHaveTheAttribute(string symbol)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: new[] { symbol });
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = framework.Concat<MetadataReference>(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        SyntaxTree input = CSharpSyntaxTree.ParseText(Source, parseOptions);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsCoverageOlderTargets",
            new[] { input },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        List<Diagnostic> errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, "Compilation errors: " + string.Join("; ", errors.Select(e => e.ToString())));

        INamedTypeSymbol? resolver = output.GetTypeByMetadataName("MyNs.MyResolver");
        Assert.NotNull(resolver);
        Assert.True(HasAttribute(resolver!), $"Resolver not decorated with ExcludeFromCodeCoverage when {symbol} is defined.");

        var uncovered = new List<string>();
        int checkedTypes = 0;
        foreach (SyntaxTree tree in output.SyntaxTrees.Where(t => !ReferenceEquals(t, input)))
        {
            SemanticModel model = output.GetSemanticModel(tree);
            foreach (ClassDeclarationSyntax cls in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (cls.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)))
                {
                    continue;
                }

                INamedTypeSymbol? type = model.GetDeclaredSymbol(cls) as INamedTypeSymbol;
                if (type is null)
                {
                    continue;
                }

                checkedTypes++;
                bool covered = false;
                for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
                {
                    if (HasAttribute(current))
                    {
                        covered = true;
                        break;
                    }
                }

                if (!covered)
                {
                    uncovered.Add(type.ToDisplayString());
                }
            }
        }

        Assert.True(checkedTypes > 0, "No generated (non-partial) types were found.");
        Assert.True(uncovered.Count == 0, $"With {symbol} defined, generated types lack ExcludeFromCodeCoverage: " + string.Join(", ", uncovered));
    }

    private static bool HasAttribute(INamedTypeSymbol symbol)
    {
        return symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == AttributeName);
    }
}
