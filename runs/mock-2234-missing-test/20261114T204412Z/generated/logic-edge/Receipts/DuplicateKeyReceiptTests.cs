// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class DuplicateKeyReceiptTests
{
    private readonly ITestOutputHelper logger;

    public DuplicateKeyReceiptTests(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    [Fact]
    public async Task DuplicateIntKey_IsReportedOrBothMembersGenerated()
    {
        string source = """
            using MessagePack;

            namespace TestNs
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver { }

                [MessagePackObject]
                public class C
                {
                    [Key(0)]
                    public int FirstValue { get; set; }

                    [Key(0)]
                    public int SecondValue { get; set; }
                }
            }
            """;
        await this.AssertReportedOrGenerated(source);
    }

    [Fact]
    public async Task DuplicateStringKey_IsReportedOrBothMembersGenerated()
    {
        string source = """
            using MessagePack;

            namespace TestNs
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver { }

                [MessagePackObject]
                public class C
                {
                    [Key("x")]
                    public int FirstValue { get; set; }

                    [Key("x")]
                    public int SecondValue { get; set; }
                }
            }
            """;
        await this.AssertReportedOrGenerated(source);
    }

    private async Task AssertReportedOrGenerated(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);
        var runResult = driver.GetRunResult();
        string generated = string.Join("\n", runResult.GeneratedTrees.Select(t => t.GetText().ToString()));

        var analyzers = typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();

        var analyzerDiagnostics = analyzers.IsEmpty
            ? ImmutableArray<Diagnostic>.Empty
            : await output.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();

        var msgPackDiagnostics = analyzerDiagnostics.Concat(generatorDiagnostics)
            .Where(d => d.Id.StartsWith("MsgPack", StringComparison.Ordinal))
            .ToList();
        foreach (var d in msgPackDiagnostics.Concat(generatorDiagnostics))
        {
            this.logger.WriteLine(d.ToString());
        }

        bool secondMemberGenerated = generated.Contains("SecondValue", StringComparison.Ordinal);
        (msgPackDiagnostics.Count > 0 || secondMemberGenerated).Should().BeTrue(
            "a duplicate key must either be reported as a diagnostic or both members must be serialized; silently dropping SecondValue loses data");
    }
}
