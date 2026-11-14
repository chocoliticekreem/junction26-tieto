// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;
using FluentAssertions;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageEdgeCaseTests
{
    private const string DataType = """
        namespace Demo
        {
            [MessagePack.MessagePackObject]
            public class MyObject
            {
                [MessagePack.Key(0)]
                public int Value { get; set; }
            }
        }

        """;

    private static async Task<(Compilation Output, ImmutableArray<Diagnostic> GeneratorDiagnostics, SyntaxTree Input)> RunAsync(string source, params string[] symbols)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: symbols);
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack).Cast<MetadataReference>()).ToList();
        var compilation = CSharpCompilation.Create("ReceiptsAsm", new[] { tree }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() }, parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diags);
        return (output, diags, tree);
    }

    [Fact]
    public async Task UserAppliedExcludeFromCodeCoverageOnResolverStillCompiles()
    {
        string source = DataType + """
            namespace Demo
            {
                [MessagePack.GeneratedMessagePackResolver]
                [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
                internal partial class MyResolver
                {
                }
            }
            """;
        var (output, genDiags, _) = await RunAsync(source);
        genDiags.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()).Should().BeEmpty();
        output.SyntaxTrees.Count().Should().BeGreaterThan(1);
        output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()).Should().BeEmpty();
    }

    [Fact]
    public async Task NetStandard20ConsumerFormatterIsExcludedFromCoverage()
    {
        var (output, genDiags, input) = await RunAsync(DataType, "NETSTANDARD2_0");
        genDiags.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()).Should().BeEmpty();
        var formatters = new List<INamedTypeSymbol>();
        foreach (var tree in output.SyntaxTrees.Where(t => !ReferenceEquals(t, input)))
        {
            var model = output.GetSemanticModel(tree);
            foreach (var cls in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(cls) is INamedTypeSymbol symbol && symbol.Name.EndsWith("Formatter", StringComparison.Ordinal))
                {
                    formatters.Add(symbol);
                }
            }
        }

        formatters.Should().NotBeEmpty();
        formatters
            .Where(s => !s.GetAttributes().Any(a => a.AttributeClass?.Name == "ExcludeFromCodeCoverageAttribute"))
            .Select(s => s.ToDisplayString())
            .Should().BeEmpty();
    }
}
