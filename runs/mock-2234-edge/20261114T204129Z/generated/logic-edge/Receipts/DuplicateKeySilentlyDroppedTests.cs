// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class DuplicateKeySilentlyDroppedTests(ITestOutputHelper logger)
{
    [Fact]
    public Task DuplicateIntKey_IsReportedOrBothMembersSerialized() => this.RunCaseAsync("[Key(0)]", "[Key(0)]");

    [Fact]
    public Task DuplicateStringKey_IsReportedOrBothMembersSerialized() => this.RunCaseAsync("[Key(\"a\")]", "[Key(\"a\")]");

    private async Task RunCaseAsync(string key1, string key2)
    {
        string source = $$"""
            using MessagePack;

            namespace TestNs
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }

                [MessagePackObject]
                public class MyObject
                {
                    {{key1}}
                    public int FirstValue { get; set; }

                    {{key2}}
                    public int SecondValue { get; set; }
                }
            }
            """;

        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3);
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var refs = Enumerable.Concat<MetadataReference>(frameworkRefs, ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        var compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { tree },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var genDiagnostics);
        var runResult = driver.GetRunResult();

        var analyzers = typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract
                && typeof(DiagnosticAnalyzer).IsAssignableFrom(t)
                && t.GetConstructor(Type.EmptyTypes) is not null
                && t.GetCustomAttributes<DiagnosticAnalyzerAttribute>().Any())
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();
        var analyzerDiagnostics = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();

        var allDiagnostics = analyzerDiagnostics.Concat(genDiagnostics).Concat(runResult.Diagnostics).ToList();
        foreach (var d in allDiagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        bool reported = allDiagnostics.Any(d =>
                (d.Id.StartsWith("MsgPack", StringComparison.Ordinal) && d.Severity == DiagnosticSeverity.Error)
                || d.Id == "AD0001"
                || d.Id == "CS8785")
            || runResult.Results.Any(r => r.Exception is not null);

        string generated = string.Join("\n", runResult.GeneratedTrees.Select(t => t.ToString()));
        logger.WriteLine(generated);
        bool bothSerialized = generated.Contains("FirstValue") && generated.Contains("SecondValue");

        Assert.True(reported || bothSerialized, "A duplicate key must be reported, or both members must be serialized; head silently drops SecondValue.");
    }
}
