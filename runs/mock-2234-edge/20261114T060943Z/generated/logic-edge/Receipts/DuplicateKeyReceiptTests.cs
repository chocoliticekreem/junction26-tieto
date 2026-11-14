// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class DuplicateKeyReceiptTests(ITestOutputHelper logger)
{
    [Fact]
    public async Task DuplicateIntKeyOnTwoProperties_IsReportedOrBothMembersSerialized()
    {
        string source = """
            using MessagePack;

            namespace TestNs
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver { }

                [MessagePackObject]
                public class MyObject
                {
                    [Key(0)]
                    public int FirstValue { get; set; }

                    [Key(0)]
                    public int SecondValue { get; set; }
                }
            }
            """;
        await AssertDuplicateHandledAsync(source);
    }

    [Fact]
    public async Task DuplicateIntKeyOnPropertyAndField_IsReportedOrBothMembersSerialized()
    {
        string source = """
            using MessagePack;

            namespace TestNs
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver { }

                [MessagePackObject]
                public class MyObject
                {
                    [Key(0)]
                    public int FirstValue { get; set; }

                    [Key(0)]
                    public int SecondValue;
                }
            }
            """;
        await AssertDuplicateHandledAsync(source);
    }

    private async Task AssertDuplicateHandledAsync(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3);
        var framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        Compilation compilation = CSharpCompilation.Create(
            "ReceiptsAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics);
        var runResult = driver.GetRunResult();

        var generatorExceptions = runResult.Results.Where(r => r.Exception is not null).Select(r => r.Exception!.ToString()).ToList();
        foreach (var e in generatorExceptions)
        {
            logger.WriteLine("Generator exception: " + e);
        }

        string generated = string.Join("\n", runResult.Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()));
        logger.WriteLine(generated);

        var analyzers = typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly.GetTypes()
            .Where(t => typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();
        var analyzerDiagnostics = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        foreach (var d in analyzerDiagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        generatorExceptions.Should().BeEmpty("the generator must not crash on duplicate keys");
        analyzerDiagnostics.Where(d => d.Id == "AD0001").Should().BeEmpty("the analyzer must not crash on duplicate keys");

        bool reported = analyzerDiagnostics.Concat(generatorDiagnostics)
            .Any(d => d.Id.StartsWith("MsgPack", StringComparison.Ordinal) && d.Severity >= DiagnosticSeverity.Warning);
        bool bothSerialized = generated.Contains("FirstValue") && generated.Contains("SecondValue");

        (reported || bothSerialized).Should().BeTrue("a member with a duplicate key must not be silently dropped from the generated formatter");
    }
}
