// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterLogicEdgeCaseTests
{
    private static async Task<(GeneratorDriverRunResult RunResult, Compilation Output)> RunGeneratorAsync(string source)
    {
        CSharpParseOptions parseOptions = new CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp12);
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        IEnumerable<MetadataReference> references = framework.Concat<MetadataReference>(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsGeneratedFormatterAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
        return (driver.GetRunResult(), output);
    }

    [Fact]
    public async Task IgnoreMemberOnFormattedTypeIsHonoured()
    {
        string source = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace Sample
            {
                public class MyType
                {
                    public int Visible { get; set; }

                    [IgnoreMember]
                    public int IgnoredSecret { get; set; }
                }

                [GeneratedMessagePackFormatter]
                public partial class MyTypeFormatter : IMessagePackFormatter<MyType> { }

                [GeneratedMessagePackResolver]
                partial class MyResolver { }
            }
            """;

        var (runResult, _) = await RunGeneratorAsync(source);
        string[] generated = runResult.GeneratedTrees.Select(t => t.ToString()).ToArray();

        Assert.Contains(generated, g => g.Contains("MyTypeFormatter") && g.Contains("Visible"));
        Assert.DoesNotContain(generated, g => g.Contains("IgnoredSecret"));
    }

    [Fact]
    public async Task NestedFormatterInNonPartialOuterIsDiagnosedOrCompiles()
    {
        string source = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace Sample
            {
                public class MyType
                {
                    public int Value { get; set; }
                }

                public class Outer
                {
                    [GeneratedMessagePackFormatter]
                    public partial class MyTypeFormatter : IMessagePackFormatter<MyType> { }
                }

                [GeneratedMessagePackResolver]
                partial class MyResolver { }
            }
            """;

        var (runResult, output) = await RunGeneratorAsync(source);
        bool generatorReportedError = runResult.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
        Diagnostic[] compileErrors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();

        Assert.True(
            generatorReportedError || compileErrors.Length == 0,
            "Generator reported no diagnostic but output does not compile: " + string.Join("; ", compileErrors.Select(d => d.ToString())));
    }
}
