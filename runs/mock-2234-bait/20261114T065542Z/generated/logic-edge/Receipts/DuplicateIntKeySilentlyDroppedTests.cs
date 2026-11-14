// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using MessagePack.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class DuplicateIntKeySilentlyDroppedTests
{
    [Fact]
    public async Task DuplicateIntKey_IsReportedOrBothMembersAreSerialized()
    {
        const string source = """
            using MessagePack;

            namespace DupNs
            {
                [MessagePackObject]
                public class DupKeys
                {
                    [Key(0)]
                    public int KeptMember { get; set; }

                    [Key(0)]
                    public int DroppedCandidate { get; set; }
                }

                [GeneratedMessagePackResolver]
                partial class DupResolver
                {
                }
            }
            """;

        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3);
        var frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var refs = new List<MetadataReference>(frameworkRefs);
        refs.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        var compilation = CSharpCompilation.Create(
            "DupKeyAsm",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new MessagePackGenerator().AsSourceGenerator() }, parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var generatorDiagnostics);
        var runResult = driver.GetRunResult();
        string generated = string.Join("\n", runResult.GeneratedTrees.Select(t => t.ToString()));

        var analyzers = typeof(MessagePackGenerator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract
                && typeof(DiagnosticAnalyzer).IsAssignableFrom(t)
                && t.GetCustomAttributes(typeof(DiagnosticAnalyzerAttribute), false).Length > 0
                && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();

        var allDiagnostics = new List<Diagnostic>(generatorDiagnostics);
        foreach (var d in runResult.Diagnostics)
        {
            allDiagnostics.Add(d);
        }

        if (analyzers.Length > 0)
        {
            var analyzerDiagnostics = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
            allDiagnostics.AddRange(analyzerDiagnostics);
        }

        bool duplicateReported = allDiagnostics.Any(d => d.Id.StartsWith("MsgPack", StringComparison.Ordinal) && d.Severity >= DiagnosticSeverity.Warning);
        bool bothMembersInFormatter = generated.Contains("KeptMember") && generated.Contains("DroppedCandidate");

        (duplicateReported || bothMembersInFormatter).Should().BeTrue(
            "two members sharing [Key(0)] must either be reported as a diagnostic or both be handled by the generated formatter; silently dropping one loses data. Diagnostics: "
            + string.Join("; ", allDiagnostics.Select(d => d.Id + " " + d.GetMessage())));
    }
}
