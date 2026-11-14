// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class DuplicateKeyMemberSilentlyDroppedTests(ITestOutputHelper logger)
{
    private const string Source = """
        using MessagePack;

        namespace Test
        {
            [MessagePackObject]
            public class MyObject
            {
                [Key(0)]
                public int AlphaValue { get; set; }

                [Key(0)]
                public int BetaValue { get; set; }
            }

            [GeneratedMessagePackResolver]
            partial class MyResolver
            {
            }
        }
        """;

    [Fact]
    public async Task DuplicateIntKeys_AreReportedOrBothMembersSerialized()
    {
        ImmutableArray<MetadataReference> frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = frameworkRefs.Concat<MetadataReference>(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();

        CSharpCompilation compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(Source, CSharpParseOptions.Default) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        ImmutableArray<DiagnosticAnalyzer> analyzers = typeof(MessagePackGenerator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();

        ImmutableArray<Diagnostic> allDiagnostics = await compilation.WithAnalyzers(analyzers).GetAllDiagnosticsAsync();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePackGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out ImmutableArray<Diagnostic> generatorDiagnostics);
        string generatedText = string.Join("\n", driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()));

        List<Diagnostic> reported = allDiagnostics
            .Where(d => d.Id.StartsWith("MsgPack", StringComparison.Ordinal) || d.Id == "AD0001")
            .Concat(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
            .ToList();

        foreach (Diagnostic d in reported)
        {
            logger.WriteLine(d.ToString());
        }

        logger.WriteLine(generatedText);

        bool betaSerialized = generatedText.Contains("BetaValue", StringComparison.Ordinal);
        Assert.True(
            reported.Count > 0 || betaSerialized,
            "Two members share [Key(0)], yet no diagnostic was reported and the generated formatter silently omits BetaValue.");
    }
}
