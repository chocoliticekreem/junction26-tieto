// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Text;

namespace Receipts.GeneratedTests;

public class PositionalRecordNullKeyDiagnosticTests
{
    [Fact]
    public async Task NullKeyOnPositionalRecordPropertyIsStillReported()
    {
        string source = """
            using MessagePack;
            namespace App
            {
                [MessagePackObject]
                public partial record Cmd([property: Key(null)] string Scope);
            }
            """;

        var framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var refs = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            "App",
            new[] { tree },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzers = typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetCustomAttributes(typeof(DiagnosticAnalyzerAttribute), false).Length > 0)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t, nonPublic: true)!)
            .ToImmutableArray();
        analyzers.Should().NotBeEmpty();

        var diagnostics = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();

        diagnostics.Should().NotContain(d => d.Id == "AD0001");

        int start = source.IndexOf("[property: Key(null)]", StringComparison.Ordinal);
        int end = source.IndexOf("Scope)", StringComparison.Ordinal) + "Scope".Length;
        var paramSpan = TextSpan.FromBounds(start, end);

        diagnostics.Should().Contain(d => d.Id != "AD0001" && d.Location.IsInSource && d.Location.SourceSpan.IntersectsWith(paramSpan));
    }
}
