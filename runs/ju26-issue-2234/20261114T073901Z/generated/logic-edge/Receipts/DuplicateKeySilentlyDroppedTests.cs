// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using FluentAssertions;
using MessagePack.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class DuplicateKeySilentlyDroppedTests(ITestOutputHelper logger)
{
    [Fact]
    public Task DuplicateIntKey_IsReportedOrBothMembersSerialized() => this.RunCaseAsync("0", "0");

    [Fact]
    public Task DuplicateStringKey_IsReportedOrBothMembersSerialized() => this.RunCaseAsync("\"k\"", "\"k\"");

    private async Task RunCaseAsync(string key1, string key2)
    {
        string source = $$"""
            using MessagePack;

            namespace N
            {
                [GeneratedMessagePackResolver]
                partial class R { }

                [MessagePackObject]
                public class Dup
                {
                    [Key({{key1}})]
                    public int FirstMember { get; set; }

                    [Key({{key2}})]
                    public int SecondMember { get; set; }
                }
            }
            """;

        var frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = frameworkRefs.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack).Cast<MetadataReference>()).ToList();
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3);
        var compilation = CSharpCompilation.Create(
            "DupKeyTest",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzers = typeof(MessagePackGenerator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetCustomAttribute<DiagnosticAnalyzerAttribute>() is not null && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();

        ImmutableArray<Diagnostic> analyzerDiagnostics = analyzers.IsEmpty
            ? ImmutableArray<Diagnostic>.Empty
            : await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePackGenerator());
        driver = driver.RunGenerators(compilation);
        GeneratorDriverRunResult result = driver.GetRunResult();

        var relevant = analyzerDiagnostics
            .Where(d => d.Id.StartsWith("MsgPack", StringComparison.Ordinal) || d.Id == "AD0001")
            .Concat(result.Diagnostics)
            .ToList();
        foreach (var d in relevant)
        {
            logger.WriteLine(d.ToString());
        }

        string generated = string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));
        logger.WriteLine(generated);

        bool reported = relevant.Count > 0;
        bool bothSerialized = generated.Contains("FirstMember") && generated.Contains("SecondMember");

        (reported || bothSerialized).Should().BeTrue("a duplicate key must either be diagnosed or both members must be serialized; silently dropping SecondMember loses data");
    }
}
