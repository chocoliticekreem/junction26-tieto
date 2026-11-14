// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageEdgeCaseTests
{
    private readonly ITestOutputHelper logger;

    public ExcludeFromCodeCoverageEdgeCaseTests(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    [Fact]
    public async Task UserResolverAlreadyExcludedFromCoverage_CompilesWithoutErrors()
    {
        string source = """
            using MessagePack;

            namespace MyApp
            {
                [GeneratedMessagePackResolver]
                [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
                partial class MyResolver
                {
                }

                [MessagePackObject]
                public class Data
                {
                    [Key(0)]
                    public int Value { get; set; }
                }
            }
            """;

        Compilation output = await this.RunGeneratorAsync(source, Array.Empty<string>());
        var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        foreach (var e in errors)
        {
            this.logger.WriteLine(e.ToString());
        }

        Assert.True(output.SyntaxTrees.Count() > 1, "generator produced no sources");
        Assert.Empty(errors);
    }

    [Fact]
    public async Task FormattersExcludedFromCoverage_WhenNetStandard20SymbolDefined()
    {
        string source = """
            using MessagePack;

            namespace MyApp
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }

                [MessagePackObject]
                public class Data
                {
                    [Key(0)]
                    public int Value { get; set; }
                }
            }
            """;

        Compilation output = await this.RunGeneratorAsync(source, new[] { "NETSTANDARD2_0", "NETSTANDARD" });
        var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        foreach (var e in errors)
        {
            this.logger.WriteLine(e.ToString());
        }

        Assert.Empty(errors);

        var formatters = AllTypes(output.Assembly.GlobalNamespace)
            .Where(t => t.AllInterfaces.Any(i => i.OriginalDefinition.ToDisplayString() == "MessagePack.Formatters.IMessagePackFormatter<T>"))
            .ToList();
        Assert.NotEmpty(formatters);
        foreach (var f in formatters)
        {
            bool hasAttribute = f.GetAttributes().Any(a => a.AttributeClass?.Name == "ExcludeFromCodeCoverageAttribute");
            Assert.True(hasAttribute, $"{f.ToDisplayString()} is not excluded from code coverage on netstandard2.0");
        }
    }

    private async Task<Compilation> RunGeneratorAsync(string source, IEnumerable<string> preprocessorSymbols)
    {
        ImmutableArray<MetadataReference> frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3, preprocessorSymbols: preprocessorSymbols);
        var references = frameworkRefs.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        foreach (var d in generatorDiagnostics)
        {
            this.logger.WriteLine("generator: " + d);
        }

        return output;
    }

    private static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceOrTypeSymbol container)
    {
        foreach (INamedTypeSymbol t in container.GetTypeMembers())
        {
            yield return t;
            foreach (INamedTypeSymbol nested in AllTypes(t))
            {
                yield return nested;
            }
        }

        if (container is INamespaceSymbol ns)
        {
            foreach (INamespaceSymbol child in ns.GetNamespaceMembers())
            {
                foreach (INamedTypeSymbol t in AllTypes(child))
                {
                    yield return t;
                }
            }
        }
    }
}
