// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.RegularExpressions;
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
    public async Task DuplicateIntKey_IsNotSilentlyDropped()
    {
        string source = """
            using MessagePack;

            namespace TestNs
            {
                [MessagePackObject]
                public class DupKeys
                {
                    [Key(0)]
                    public int A { get; set; }

                    [Key(0)]
                    public int B { get; set; }
                }

                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }
            }
            """;

        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3);
        ImmutableArray<MetadataReference> frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        IEnumerable<MetadataReference> allRefs = frameworkRefs.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            allRefs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);

        ImmutableArray<DiagnosticAnalyzer> analyzers = typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();

        ImmutableArray<Diagnostic> analyzerAndCompilerDiagnostics = analyzers.IsEmpty
            ? output.GetDiagnostics()
            : await output.WithAnalyzers(analyzers).GetAllDiagnosticsAsync();

        List<Diagnostic> all = generatorDiagnostics.Concat(analyzerAndCompilerDiagnostics).ToList();
        foreach (Diagnostic d in all)
        {
            this.logger.WriteLine(d.ToString());
        }

        string generated = string.Join("\n", output.SyntaxTrees.Skip(1).Select(t => t.ToString()));
        this.logger.WriteLine(generated);

        bool reported = all.Any(d => d.Severity == DiagnosticSeverity.Error || d.Id.StartsWith("MsgPack", StringComparison.Ordinal) || d.Id == "CS8785" || d.Id == "AD0001");
        bool memberBSerialized = Regex.IsMatch(generated, @"\bB\b");

        Assert.True(
            reported || memberBSerialized,
            "Two members share [Key(0)], yet no diagnostic was reported and the generated formatter does not reference member B: B is silently dropped from serialization.");
    }
}
