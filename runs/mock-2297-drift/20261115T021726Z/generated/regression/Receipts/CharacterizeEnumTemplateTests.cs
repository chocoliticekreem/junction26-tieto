// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class CharacterizeEnumTemplateTests
{
    private static async Task<(Compilation Output, List<string> Generated)> RunGeneratorAsync(string source)
    {
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        IEnumerable<MetadataReference> references = framework.AsEnumerable().Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsEnumTestAssembly",
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
    public async Task EnumMember_ProducesCompilingEnumFormatter()
    {
        string source = """
            using MessagePack;

            namespace MyApp
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }

                public enum MyColor : byte
                {
                    Red,
                    Green,
                }

                [MessagePackObject]
                public class Holder
                {
                    [Key(0)]
                    public MyColor Color { get; set; }
                }
            }
            """;

        var (output, generated) = await RunGeneratorAsync(source);

        generated.Should().Contain(s => s.Contains("MyColorFormatter"));
        output.GetSymbolsWithName("MyColorFormatter", SymbolFilter.Type).OfType<INamedTypeSymbol>()
            .Any(t => t.AllInterfaces.Any(i => i.Name == "IMessagePackFormatter" && i.TypeArguments.Length == 1 && i.TypeArguments[0].ToDisplayString() == "MyApp.MyColor"))
            .Should().BeTrue();
    }

    [Fact]
    public async Task ByteEnum_FormatterReadsUnderlyingByte()
    {
        string source = """
            using MessagePack;

            namespace MyApp
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }

                public enum MyColor : byte
                {
                    Red,
                    Green,
                }

                [MessagePackObject]
                public class Holder
                {
                    [Key(0)]
                    public MyColor Color { get; set; }
                }
            }
            """;

        var (_, generated) = await RunGeneratorAsync(source);

        generated.Should().Contain(s => s.Contains("MyColorFormatter") && s.Contains("reader.ReadByte()"));
    }
}
