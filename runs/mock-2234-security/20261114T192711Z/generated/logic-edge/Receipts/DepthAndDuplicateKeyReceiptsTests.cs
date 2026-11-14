// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class DepthAndDuplicateKeyReceiptsTests
{
    [Fact]
    public void DepthStep_AtMaximumDepth_Throws()
    {
        var security = global::MessagePack.MessagePackSecurity.UntrustedData.WithMaximumObjectGraphDepth(3);
        var reader = new global::MessagePack.MessagePackReader(new ReadOnlyMemory<byte>(new byte[] { 0xc0 }));
        reader.Depth = 3;
        bool threw = false;
        try
        {
            security.DepthStep(ref reader);
        }
        catch (InsufficientExecutionStackException)
        {
            threw = true;
        }

        Assert.True(threw, "DepthStep must throw once the reader is already at MaximumObjectGraphDepth.");
    }

    [Fact]
    public async Task DuplicateIntKey_IsReportedOrBothMembersSerialized()
    {
        string source = """
            using MessagePack;

            [MessagePackObject]
            public class DupHolder
            {
                [Key(0)] public int FirstValue { get; set; }
                [Key(0)] public int SecondValue { get; set; }
            }

            [GeneratedMessagePackResolver]
            partial class DupResolver { }
            """;

        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var allRefs = frameworkRefs.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        var compilation = CSharpCompilation.Create(
            "DupAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            allRefs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out var generatorDiagnostics);
        var runResult = driver.GetRunResult();
        string generated = string.Join("\n", runResult.GeneratedTrees.Select(t => t.ToString()));

        var analyzers = typeof(global::MessagePack.SourceGenerator.MessagePackGenerator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract
                && typeof(DiagnosticAnalyzer).IsAssignableFrom(t)
                && t.GetCustomAttributes(typeof(DiagnosticAnalyzerAttribute), false).Length > 0
                && t.GetConstructor(Type.EmptyTypes) != null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();

        var analyzerDiagnostics = analyzers.IsEmpty
            ? ImmutableArray<Diagnostic>.Empty
            : await output.WithAnalyzers(analyzers).GetAllDiagnosticsAsync();

        bool hasMsgPackError = generatorDiagnostics
            .Concat(runResult.Diagnostics)
            .Concat(analyzerDiagnostics)
            .Any(d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("MsgPack", StringComparison.Ordinal));

        Assert.True(
            hasMsgPackError || generated.Contains("SecondValue"),
            "A duplicate [Key(0)] must either be reported as an error or both members must be serialized; head silently drops SecondValue.");
    }
}
