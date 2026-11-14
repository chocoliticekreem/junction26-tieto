// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;
using MessagePack.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ReqExcludeFromCodeCoverageGapTests
{
    private const string Source = """
        using MessagePack;

        namespace TestNs
        {
            [GeneratedMessagePackResolver]
            partial class MyResolver { }

            public enum MyEnum { A, B }

            [MessagePackObject]
            public class IntKeyed
            {
                [Key(0)] public int Value { get; set; }
                [Key(1)] public MyEnum E { get; set; }
                [Key(2)] public StringKeyed S { get; set; }
            }

            [MessagePackObject(true)]
            public class StringKeyed
            {
                public int Value { get; set; }
            }
        }
        """;

    [Fact]
    public async Task EnumFormatter_IsExcludedFromCodeCoverage_OnModernTarget()
    {
        List<INamedTypeSymbol> types = await GenerateAsync();
        INamedTypeSymbol? formatter = FindFormatterFor(types, "MyEnum");
        Assert.True(formatter is not null, "No generated formatter found for MyEnum.");
        Assert.True(HasExcludeFromCodeCoverage(formatter!), $"Generated enum formatter {formatter!.ToDisplayString()} lacks [ExcludeFromCodeCoverage] on a net8.0 target.");
    }

    [Fact]
    public async Task Formatters_AreExcludedFromCodeCoverage_WhenTargetingNetStandard20()
    {
        List<INamedTypeSymbol> types = await GenerateAsync("NETSTANDARD2_0");
        foreach (string dataType in new[] { "IntKeyed", "StringKeyed" })
        {
            INamedTypeSymbol? formatter = FindFormatterFor(types, dataType);
            Assert.True(formatter is not null, $"No generated formatter found for {dataType}.");
            Assert.True(HasExcludeFromCodeCoverage(formatter!), $"Generated formatter {formatter!.ToDisplayString()} lacks [ExcludeFromCodeCoverage] when NETSTANDARD2_0 is defined.");
        }

        INamedTypeSymbol? resolver = types.FirstOrDefault(t => t.Name == "MyResolver");
        Assert.True(resolver is not null, "Generated resolver MyResolver not found.");
        Assert.True(HasExcludeFromCodeCoverage(resolver!), "Generated resolver lacks [ExcludeFromCodeCoverage] when NETSTANDARD2_0 is defined.");
    }

    private static bool HasExcludeFromCodeCoverage(INamedTypeSymbol symbol)
        => symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute");

    private static INamedTypeSymbol? FindFormatterFor(IEnumerable<INamedTypeSymbol> types, string dataTypeName)
        => types.FirstOrDefault(t => t.AllInterfaces.Any(i => i.Name == "IMessagePackFormatter" && i.TypeArguments.Length == 1 && i.TypeArguments[0].Name == dataTypeName));

    private static async Task<List<INamedTypeSymbol>> GenerateAsync(params string[] preprocessorSymbols)
    {
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: preprocessorSymbols);
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = Enumerable.Concat<MetadataReference>(framework, ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsGapTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(Source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new MessagePackGenerator().AsSourceGenerator() }, parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> _);
        GeneratorDriverRunResult run = driver.GetRunResult();
        Assert.Empty(run.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        HashSet<string> generatedPaths = new HashSet<string>(run.GeneratedTrees.Select(t => t.FilePath));
        List<INamedTypeSymbol> result = new List<INamedTypeSymbol>();
        foreach (SyntaxTree tree in output.SyntaxTrees.Where(t => generatedPaths.Contains(t.FilePath)))
        {
            SemanticModel model = output.GetSemanticModel(tree);
            foreach (TypeDeclarationSyntax decl in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(decl) is INamedTypeSymbol symbol)
                {
                    result.Add(symbol);
                }
            }
        }

        Assert.NotEmpty(result);
        return result;
    }
}
