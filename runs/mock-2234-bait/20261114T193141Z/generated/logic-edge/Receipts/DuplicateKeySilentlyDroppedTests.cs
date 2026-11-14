// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class DuplicateKeySilentlyDroppedTests(ITestOutputHelper logger)
{
    [Fact]
    public async Task DuplicateIntKey_IsReportedOrSerialized()
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

        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = frameworkRefs.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();

        CSharpCompilation compilation = CSharpCompilation.Create(
            "DuplicateKeyTest",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out ImmutableArray<Diagnostic> generatorDiagnostics);
        GeneratorDriverRunResult runResult = driver.GetRunResult();

        string generated = string.Join("\n", runResult.GeneratedTrees.Select(t => t.ToString()));
        logger.WriteLine(generated);

        var analyzers = typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetCustomAttributes(typeof(DiagnosticAnalyzerAttribute), false).Length > 0)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();

        ImmutableArray<Diagnostic> analyzerDiagnostics = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();

        var relevant = generatorDiagnostics
            .Concat(runResult.Diagnostics)
            .Concat(analyzerDiagnostics)
            .Where(d => d.Id.StartsWith("MsgPack", StringComparison.Ordinal) || d.Id == "CS8785" || d.Id == "AD0001")
            .ToList();

        foreach (Diagnostic d in relevant)
        {
            logger.WriteLine(d.ToString());
        }

        Assert.True(
            generated.Contains("SecondValue") || relevant.Count > 0,
            "A member with a duplicate [Key(0)] was silently dropped: no diagnostic was reported and the generated formatter does not serialize SecondValue.");
    }
}
