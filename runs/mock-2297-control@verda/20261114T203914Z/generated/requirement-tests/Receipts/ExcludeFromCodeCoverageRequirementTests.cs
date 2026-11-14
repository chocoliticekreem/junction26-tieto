// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageRequirementTests
{
    [Fact]
    public async Task FormatterAndResolverSources_AreDecoratedWithExcludeFromCodeCoverage()
    {
        const string source = """
            using MessagePack;

            [MessagePackObject]
            public class Sample
            {
                [Key(0)]
                public int Value { get; set; }
            }

            [GeneratedMessagePackResolver]
            internal partial class SampleResolver
            {
            }
            """;

        string[] generatedSources = await GenerateAsync(source);

        Assert.NotEmpty(generatedSources);
        Assert.All(
            generatedSources,
            text => Assert.Contains("global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage", text));
    }

    [Fact]
    public async Task StringKeyEnumUnionAndResolverSources_AreDecoratedWithExcludeFromCodeCoverage()
    {
        const string source = """
            using MessagePack;

            public enum SampleEnum
            {
                Unknown,
                Value,
            }

            [MessagePackObject(true)]
            public class StringKeySample
            {
                public int Id { get; set; }
            }

            [MessagePackObject]
            public class EnumContainer
            {
                [Key(0)]
                public SampleEnum Value { get; set; }
            }

            [Union(0, typeof(FirstUnionMember))]
            [Union(1, typeof(SecondUnionMember))]
            public interface IUnionSample
            {
            }

            [MessagePackObject]
            public sealed class FirstUnionMember : IUnionSample
            {
                [Key(0)]
                public int Value { get; set; }
            }

            [MessagePackObject]
            public sealed class SecondUnionMember : IUnionSample
            {
                [Key(0)]
                public int Value { get; set; }
            }

            [GeneratedMessagePackResolver]
            internal partial class SampleResolver
            {
            }
            """;

        string[] generatedSources = await GenerateAsync(source);

        Assert.NotEmpty(generatedSources);
        Assert.All(
            generatedSources,
            text => Assert.Contains("global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage", text));
    }

    private static async Task<string[]> GenerateAsync(string source)
    {
        MetadataReference[] references = (await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None))
            .Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack))
            .ToArray();
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp12);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ExcludeFromCodeCoverageRequirementCompilation",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator(),
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation updatedCompilation,
            out ImmutableArray<Diagnostic> generatorDiagnostics);

        Assert.Empty(generatorDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Diagnostic[] compilationErrors = updatedCompilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.True(
            compilationErrors.Length == 0,
            string.Join(Environment.NewLine, compilationErrors.Select(diagnostic => diagnostic.ToString())));

        return driver.GetRunResult().Results
            .SelectMany(result => result.GeneratedSources)
            .Select(generatedSource => generatedSource.SourceText.ToString())
            .ToArray();
    }
}
