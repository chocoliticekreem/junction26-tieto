// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class DuplicateKeyReceipts(ITestOutputHelper logger)
{
    [Fact]
    public async Task DuplicateIntKey_ReportsSomeMsgPackDiagnostic()
    {
        string source = """
            using MessagePack;

            namespace TestNs
            {
                [MessagePackObject]
                public class Dup
                {
                    [Key(0)]
                    public int FirstValue { get; set; }

                    [Key(0)]
                    public int SecondValue { get; set; }
                }
            }
            """;

        var framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var refs = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        var compilation = CSharpCompilation.Create(
            "DupKeyTest",
            new[] { CSharpSyntaxTree.ParseText(source) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzers = typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();
        analyzers.Should().NotBeEmpty();

        var diags = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        foreach (var d in diags)
        {
            logger.WriteLine(d.ToString());
        }

        diags.Where(d => d.Id.StartsWith("MsgPack", StringComparison.Ordinal)).Should().NotBeEmpty("two members share Key(0); one would be silently dropped from serialization");
    }
}
