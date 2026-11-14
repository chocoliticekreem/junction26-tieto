// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedResolverCoverageAttributeTests
{
    private const string ExcludeFromCodeCoverageAttribute = "[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]";

    private const string TestSource = """
        using MessagePack;

        [MessagePackObject]
        public class MyObject
        {
            [Key(0)]
            public int Value { get; set; }
        }

        [GeneratedMessagePackResolver]
        public partial class TestResolver
        {
        }
        """;

    [Fact]
    public async Task EveryGeneratedSourceFile_HasExcludeFromCodeCoverage()
    {
        GeneratorDriverRunResult result = await RunGeneratorAsync();

        Assert.NotEmpty(result.GeneratedTrees);
        foreach (SyntaxTree generatedTree in result.GeneratedTrees)
        {
            Assert.True(
                generatedTree.ToString().Contains(ExcludeFromCodeCoverageAttribute, StringComparison.Ordinal),
                $"Generated file '{generatedTree.FilePath}' has no ExcludeFromCodeCoverage attribute.");
        }
    }

    private static async Task<GeneratorDriverRunResult> RunGeneratorAsync()
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        SyntaxTree inputTree = CSharpSyntaxTree.ParseText(TestSource, parseOptions);
        ImmutableArray<MetadataReference> frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        IEnumerable<MetadataReference> references = frameworkReferences.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Receipts.GeneratedTests.ResolverInput",
            new[] { inputTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new ISourceGenerator[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out ImmutableArray<Diagnostic> generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        return driver.GetRunResult();
    }
}
