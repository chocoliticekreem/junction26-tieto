// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class CharacterizeUnionTemplateTests
{
    private static async Task<(Compilation Output, List<string> Generated)> RunGeneratorAsync(string source)
    {
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        IEnumerable<MetadataReference> references = framework.AsEnumerable().Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsUnionTestAssembly",
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

    private const string UnionSource = """
        using MessagePack;

        namespace MyApp
        {
            [GeneratedMessagePackResolver]
            partial class MyResolver
            {
            }

            [Union(0, typeof(Dog))]
            [Union(1, typeof(Cat))]
            public interface IAnimal
            {
            }

            [MessagePackObject]
            public class Dog : IAnimal
            {
                [Key(0)]
                public int Barks { get; set; }
            }

            [MessagePackObject]
            public class Cat : IAnimal
            {
                [Key(0)]
                public int Lives { get; set; }
            }
        }
        """;

    [Fact]
    public async Task UnionInterface_ProducesCompilingUnionFormatter()
    {
        var (output, _) = await RunGeneratorAsync(UnionSource);

        output.GetSymbolsWithName("IAnimalFormatter", SymbolFilter.Type).OfType<INamedTypeSymbol>()
            .Any(t => t.AllInterfaces.Any(i => i.Name == "IMessagePackFormatter" && i.TypeArguments.Length == 1 && i.TypeArguments[0].ToDisplayString() == "MyApp.IAnimal"))
            .Should().BeTrue();
    }

    [Fact]
    public async Task UnionFormatter_ReferencesAllSubTypes()
    {
        var (_, generated) = await RunGeneratorAsync(UnionSource);

        generated.Should().Contain(s => s.Contains("IAnimalFormatter") && s.Contains("MyApp.Dog") && s.Contains("MyApp.Cat") && s.Contains("RuntimeTypeHandleEqualityComparer"));
    }
}
