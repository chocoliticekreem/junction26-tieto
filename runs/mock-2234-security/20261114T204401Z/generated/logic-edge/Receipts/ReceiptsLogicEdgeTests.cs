// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack;
using MessagePack.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ReceiptsLogicEdgeTests
{
    [Fact]
    public void DepthStep_RejectsGraphOneLevelDeeperThanMaximum()
    {
        MessagePackSerializerOptions options = MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.UntrustedData.WithMaximumObjectGraphDepth(1));

        // [[ ]] : two nested arrays, depth 2, which exceeds a maximum depth of 1.
        byte[] bytes = new byte[] { 0x91, 0x90 };

        Assert.ThrowsAny<Exception>(() => MessagePackSerializer.Deserialize<object>(bytes, options));
    }

    [Fact]
    public async Task DuplicateIntKey_IsReportedOrBothMembersGenerated()
    {
        string source = """
            using MessagePack;

            namespace DupNs
            {
                [MessagePackObject]
                public class DupKeys
                {
                    [Key(0)]
                    public int FirstMember { get; set; }

                    [Key(0)]
                    public int SecondMember { get; set; }
                }

                [GeneratedMessagePackResolver]
                partial class DupResolver
                {
                }
            }
            """;

        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        IEnumerable<MetadataReference> references = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "DupKeyAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp7_3)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new MessagePackGenerator().AsSourceGenerator() });
        driver = driver.RunGenerators(compilation);
        GeneratorDriverRunResult result = driver.GetRunResult();
        string generated = string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));

        ImmutableArray<DiagnosticAnalyzer> analyzers = typeof(MessagePackGenerator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();

        ImmutableArray<Diagnostic> analyzerDiagnostics = analyzers.IsEmpty
            ? ImmutableArray<Diagnostic>.Empty
            : await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();

        bool reported = result.Diagnostics.Any(d => d.Severity >= DiagnosticSeverity.Warning)
            || analyzerDiagnostics.Any(d => d.Id.StartsWith("MsgPack", StringComparison.Ordinal) && d.Severity >= DiagnosticSeverity.Warning);

        Assert.True(
            reported || generated.Contains("SecondMember"),
            "Two members share [Key(0)]: expected either a diagnostic or a formatter that handles SecondMember, but SecondMember was silently dropped.");
    }
}
