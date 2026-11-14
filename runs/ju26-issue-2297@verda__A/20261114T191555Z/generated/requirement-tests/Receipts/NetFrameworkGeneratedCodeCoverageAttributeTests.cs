// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class NetFrameworkGeneratedCodeCoverageAttributeTests
{
    [Fact]
    public async Task NetFrameworkGeneratedCodeIncludesAvailableAttribute()
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

        var parseOptions = new CSharpParseOptions(
            LanguageVersion.CSharp7_3,
            preprocessorSymbols: new[] { "NETFRAMEWORK", "NET472" });
        ImmutableArray<MetadataReference> references = await ReferenceAssemblies.NetFramework.Net472.Default.ResolveAsync(
            LanguageNames.CSharp,
            CancellationToken.None);
        references = references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: "NetFrameworkCoverageReceipts",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        ISourceGenerator generator = new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { generator }, parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> generatorDiagnostics);
        GeneratorRunResult runResult = Assert.Single(driver.GetRunResult().Results);

        Assert.Empty(generatorDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.NotEmpty(runResult.GeneratedSources);
        Assert.All(runResult.GeneratedSources, generatedSource =>
            Assert.Contains(
                "[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]",
                generatedSource.SourceText.ToString()));
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
