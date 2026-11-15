// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class CharacterizeEmitNestingTypesAndNamespacesTests
{
    private static async Task<(Compilation Output, List<string> Generated)> RunGeneratorAsync(string source)
    {
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        IEnumerable<MetadataReference> references = framework.AsEnumerable().Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator()).WithUpdatedParseOptions(parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        GeneratorDriverRunResult run = driver.GetRunResult();
        run.Results.Select(r => r.Exception?.ToString()).Where(e => e is not null).Should().BeEmpty();
        generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()).Should().BeEmpty();
        output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()).Should().BeEmpty();
        List<string> generated = run.GeneratedTrees.Select(t => t.GetText().ToString()).ToList();
        return (output, generated);
    }

    [Fact]
    public async Task NamespacedResolver_GeneratedCodeCompilesAndDeclaresResolverInNamespace()
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
                public class MyObject
                {
                    [Key(0)]
                    public int Value { get; set; }
                }
            }
            """;

        var (output, generated) = await RunGeneratorAsync(source);

        generated.Should().Contain(s => s.Contains("namespace MyApp") && s.Contains("MyResolver"));
        INamedTypeSymbol? resolver = output.GetTypeByMetadataName("MyApp.MyResolver");
        resolver.Should().NotBeNull();
        resolver!.AllInterfaces.Any(i => i.Name == "IFormatterResolver").Should().BeTrue();
        resolver.GetMembers("Instance").Should().NotBeEmpty();
    }

    [Fact]
    public async Task GlobalNamespaceResolver_FormattersNestedUnderResolverCompile()
    {
        string source = """
            using MessagePack;

            [GeneratedMessagePackResolver]
            partial class MyResolver
            {
            }

            namespace MyApp
            {
                [MessagePackObject]
                public class MyObject
                {
                    [Key(0)]
                    public int Value { get; set; }
                }
            }
            """;

        var (output, _) = await RunGeneratorAsync(source);

        INamedTypeSymbol? resolver = output.GetTypeByMetadataName("MyResolver");
        resolver.Should().NotBeNull();
        resolver!.AllInterfaces.Any(i => i.Name == "IFormatterResolver").Should().BeTrue();
        output.GetSymbolsWithName("MyObjectFormatter", SymbolFilter.Type).OfType<INamedTypeSymbol>()
            .Any(t => t.AllInterfaces.Any(i => i.Name == "IMessagePackFormatter" && i.TypeArguments.Length == 1 && i.TypeArguments[0].ToDisplayString() == "MyApp.MyObject"))
            .Should().BeTrue();
    }
}
