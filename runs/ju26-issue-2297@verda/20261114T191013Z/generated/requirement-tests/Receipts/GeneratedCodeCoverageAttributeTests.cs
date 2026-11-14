// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedCodeCoverageAttributeTests
{
    private const string AttributeText = "[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]";

    [Fact]
    public async Task AllGeneratedSourceFilesContainExcludeFromCodeCoverage()
    {
        const string source = """
            using MessagePack;

            [MessagePackObject]
            public partial class IntKeyObject
            {
                [Key(0)]
                public int Value { get; set; }

                [Key(1)]
                public SampleEnum EnumValue { get; set; }
            }

            [MessagePackObject(true)]
            public partial class StringKeyObject
            {
                public int Value { get; set; }
            }

            public enum SampleEnum
            {
                None,
                One,
            }

            [MessagePackObject]
            [Union(0, typeof(FirstUnion))]
            [Union(1, typeof(SecondUnion))]
            public partial interface IUnion
            {
            }

            [MessagePackObject]
            public partial class FirstUnion : IUnion
            {
                [Key(0)]
                public int Value { get; set; }
            }

            [MessagePackObject]
            public partial class SecondUnion : IUnion
            {
                [Key(0)]
                public string Name { get; set; }
            }

            [GeneratedMessagePackResolver]
            public partial class TestResolver
            {
            }
            """;

        CSharpCompilation compilation = await CreateCompilationAsync(source, ReferenceAssemblies.Net.Net80);
        (CSharpCompilation outputCompilation, GeneratorRunResult runResult) = RunGenerator(compilation);

        Assert.NotEmpty(runResult.GeneratedSources);
        Assert.All(runResult.GeneratedSources, generatedSource =>
            Assert.Contains(AttributeText, generatedSource.SourceText.ToString()));
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task NetStandard20GeneratedCodeOmitsUnavailableAttributeAndCompiles()
    {
        const string source = """
            using MessagePack;

            [MessagePackObject]
            public partial class MyObject
            {
                [Key(0)]
                public int Value { get; set; }
            }

            [GeneratedMessagePackResolver]
            public partial class TestResolver
            {
            }
            """;

        CSharpCompilation compilation = await CreateCompilationAsync(
            source,
            ReferenceAssemblies.NetStandard.NetStandard20,
            "NETSTANDARD2_0");
        (CSharpCompilation outputCompilation, GeneratorRunResult runResult) = RunGenerator(compilation);

        Assert.NotEmpty(runResult.GeneratedSources);
        Assert.All(runResult.GeneratedSources, generatedSource =>
            Assert.DoesNotContain("ExcludeFromCodeCoverage", generatedSource.SourceText.ToString()));
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static async Task<CSharpCompilation> CreateCompilationAsync(
        string source,
        ReferenceAssemblies referenceAssemblies,
        params string[] preprocessorSymbols)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3, preprocessorSymbols: preprocessorSymbols);
        ImmutableArray<MetadataReference> references = await referenceAssemblies.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        references = references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        return CSharpCompilation.Create(
            assemblyName: "CoverageAttributeReceipts",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static (CSharpCompilation Compilation, GeneratorRunResult RunResult) RunGenerator(CSharpCompilation compilation)
    {
        var parseOptions = (CSharpParseOptions)compilation.SyntaxTrees.Single().Options;
        ISourceGenerator generator = new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { generator }, parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> generatorDiagnostics);

        Assert.Empty(generatorDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        return ((CSharpCompilation)outputCompilation, Assert.Single(driver.GetRunResult().Results));
    }
}
