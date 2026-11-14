// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterLogicReceiptTests
{
    private static async Task<(GeneratorDriverRunResult Run, Compilation Output, ImmutableArray<Diagnostic> GeneratorDiagnostics)> RunGeneratorAsync(string source)
    {
        List<MetadataReference> references = new();
        references.AddRange(await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None));
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
        return (driver.GetRunResult(), output, diagnostics);
    }

    [Fact]
    public async Task ContractlessMode_HonoursIgnoreMember()
    {
        string source = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace TestNs
            {
                public class MyObject
                {
                    public int VisibleValue { get; set; }

                    [IgnoreMember]
                    public int SecretValue { get; set; }
                }

                [GeneratedMessagePackFormatter]
                public partial class MyObjectFormatter : IMessagePackFormatter<MyObject> { }

                [GeneratedMessagePackResolver]
                partial class MyResolver { }
            }
            """;

        var (run, _, _) = await RunGeneratorAsync(source);
        string allGenerated = string.Join("\n", run.GeneratedTrees.Select(t => t.GetText().ToString()));

        Assert.Contains("VisibleValue", allGenerated);
        Assert.DoesNotContain("SecretValue", allGenerated);
    }

    [Fact]
    public async Task NestedFormatterInNonPartialContainer_ReportsDiagnosticOrCompiles()
    {
        string source = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace TestNs
            {
                public class MyObject
                {
                    public int Value { get; set; }
                }

                public class Outer
                {
                    [GeneratedMessagePackFormatter]
                    public partial class MyObjectFormatter : IMessagePackFormatter<MyObject> { }
                }

                [GeneratedMessagePackResolver]
                partial class MyResolver { }
            }
            """;

        var (_, output, generatorDiagnostics) = await RunGeneratorAsync(source);
        bool generatorReportedError = generatorDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
        List<Diagnostic> compileErrors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

        Assert.True(
            generatorReportedError || compileErrors.Count == 0,
            "Expected a generator diagnostic or a clean compilation, but got compiler errors: " + string.Join("; ", compileErrors.Select(d => d.ToString())));
    }
}
