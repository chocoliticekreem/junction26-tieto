// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class CharacterizeGetAnalyzerOptionTests
{
    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Latest);

    private static async Task<(Compilation Output, GeneratorDriverRunResult Run)> RunGeneratorAsync(string source)
    {
        var frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var refs = frameworkRefs.Cast<MetadataReference>().Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack).Cast<MetadataReference>()).ToList();
        var compilation = CSharpCompilation.Create("TestAssembly", new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) }, refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ISourceGenerator[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() }, parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> _);
        GeneratorDriverRunResult run = driver.GetRunResult();
        Assert.All(run.Results, r => Assert.Null(r.Exception));
        Assert.DoesNotContain(run.Diagnostics, d => d.Id == "CS8785");
        return (output, run);
    }

    private static List<string> Sources(GeneratorDriverRunResult run)
    {
        return run.Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()).ToList();
    }

    private static void AssertNoErrors(Compilation output)
    {
        var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors.Select(e => e.ToString())));
    }

    [Fact]
    public async Task LocalFormatterImplementation_IsPickedUpIntoGeneratedResolver()
    {
        string source = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace MyNs
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver { }
            }

            public class CustomType
            {
                public int X;
            }

            public sealed class CustomTypeFormatter : IMessagePackFormatter<CustomType>
            {
                public void Serialize(ref MessagePackWriter writer, CustomType value, MessagePackSerializerOptions options) => writer.Write(value.X);

                public CustomType Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) => new CustomType { X = reader.ReadInt32() };
            }

            [MessagePackObject]
            public class Holder
            {
                [Key(0)]
                public CustomType Value { get; set; }
            }
            """;
        var (output, run) = await RunGeneratorAsync(source);
        AssertNoErrors(output);
        List<string> sources = Sources(run);
        Assert.Contains(sources, s => s.Contains("HolderFormatter"));
        Assert.Contains(sources, s => s.Contains("CustomTypeFormatter"));
    }

    [Fact]
    public async Task WithoutResolverAttribute_DefaultOptionsStillGenerateFormatter()
    {
        string source = """
            using MessagePack;

            [MessagePackObject]
            public class Holder
            {
                [Key(0)]
                public int Value { get; set; }
            }
            """;
        var (output, run) = await RunGeneratorAsync(source);
        AssertNoErrors(output);
        Assert.Contains(Sources(run), s => s.Contains("HolderFormatter"));
    }
}
