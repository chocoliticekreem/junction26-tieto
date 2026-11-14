// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class DuplicateKeyDiagnosticReceipts
{
    private static async Task<List<Diagnostic>> RunAllAsync(string source)
    {
        var framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var refs = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        var compilation = CSharpCompilation.Create(
            "DupKeyTest",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp7_3)) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var asm = typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly;
        var analyzers = asm.GetTypes()
            .Where(t => typeof(Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();

        var all = new List<Diagnostic>();
        if (analyzers.Length > 0)
        {
            var withAnalyzers = compilation.WithAnalyzers(analyzers);
            all.AddRange(await withAnalyzers.GetAnalyzerDiagnosticsAsync());
        }

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var genDiags);
        all.AddRange(genDiags);
        return all;
    }

    private static void AssertMsgPackError(List<Diagnostic> diags)
    {
        Assert.True(
            diags.Any(d => d.Id.StartsWith("MsgPack", StringComparison.Ordinal) && d.Severity == DiagnosticSeverity.Error),
            "Expected an Error-severity MsgPack diagnostic for duplicate keys, got: " + string.Join("; ", diags.Select(d => d.Id + ": " + d.GetMessage())));
    }

    [Fact]
    public async Task DuplicateIntKeys_ReportsMsgPackError()
    {
        string source = """
            using MessagePack;
            namespace TestNs
            {
                [MessagePackObject]
                public class Dup
                {
                    [Key(0)] public int A { get; set; }
                    [Key(0)] public int B { get; set; }
                }
            }
            """;
        AssertMsgPackError(await RunAllAsync(source));
    }

    [Fact]
    public async Task DuplicateStringKeys_ReportsMsgPackError()
    {
        string source = """
            using MessagePack;
            namespace TestNs
            {
                [MessagePackObject]
                public class Dup
                {
                    [Key("a")] public int A { get; set; }
                    [Key("a")] public int B { get; set; }
                }
            }
            """;
        AssertMsgPackError(await RunAllAsync(source));
    }

    [Fact]
    public async Task DuplicateIntKeysOnFields_ReportsMsgPackError()
    {
        string source = """
            using MessagePack;
            namespace TestNs
            {
                [MessagePackObject]
                public class Dup
                {
                    [Key(0)] public int A;
                    [Key(0)] public int B;
                }
            }
            """;
        AssertMsgPackError(await RunAllAsync(source));
    }
}
