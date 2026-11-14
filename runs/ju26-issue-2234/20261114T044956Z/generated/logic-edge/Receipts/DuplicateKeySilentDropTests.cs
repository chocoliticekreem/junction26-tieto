// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using MessagePack.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class DuplicateKeySilentDropTests
{
    private const string Source = @"
using MessagePack;

namespace TestNs
{
    [GeneratedMessagePackResolver]
    partial class MyResolver
    {
    }

    [MessagePackObject]
    public class DupKeys
    {
        [Key(0)]
        public int FirstValue { get; set; }

        [Key(0)]
        public int SecondValue { get; set; }
    }
}
";

    [Fact]
    public async Task DuplicateIntKeys_SecondMemberIsNeitherGeneratedNorDiagnosed()
    {
        ImmutableArray<MetadataReference> frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> allRefs = frameworkRefs.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();

        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "DupKeyAsm",
            new[] { CSharpSyntaxTree.ParseText(Source, parseOptions) },
            allRefs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation _, out ImmutableArray<Diagnostic> generatorDiagnostics);
        GeneratorDriverRunResult runResult = driver.GetRunResult();
        string generatedText = string.Join("\n", runResult.GeneratedTrees.Select(t => t.ToString()));

        List<DiagnosticAnalyzer> analyzers = new List<DiagnosticAnalyzer>();
        foreach (Type t in typeof(MessagePackGenerator).Assembly.GetTypes())
        {
            if (t.IsAbstract || !typeof(DiagnosticAnalyzer).IsAssignableFrom(t) || t.GetCustomAttributes(typeof(DiagnosticAnalyzerAttribute), false).Length == 0)
            {
                continue;
            }

            try
            {
                if (Activator.CreateInstance(t) is DiagnosticAnalyzer a)
                {
                    analyzers.Add(a);
                }
            }
            catch (MissingMethodException)
            {
            }
        }

        ImmutableArray<Diagnostic> analyzerDiagnostics = analyzers.Count == 0
            ? ImmutableArray<Diagnostic>.Empty
            : await compilation.WithAnalyzers(analyzers.ToImmutableArray()).GetAnalyzerDiagnosticsAsync();

        bool errorReported = analyzerDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error && d.Id.StartsWith("MsgPack", StringComparison.Ordinal))
            || generatorDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
        bool secondMemberGenerated = generatedText.Contains("SecondValue", StringComparison.Ordinal);

        Assert.True(
            errorReported || secondMemberGenerated,
            "Duplicate [Key(0)] on FirstValue and SecondValue: SecondValue was silently dropped from the generated formatter and no MsgPack error was reported. Analyzer diagnostics: "
            + string.Join(", ", analyzerDiagnostics.Select(d => d.Id))
            + "; generator diagnostics: " + string.Join(", ", generatorDiagnostics.Select(d => d.Id)));
    }
}
