// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Receipts.GeneratedTests;

public class SuppressSourceGenerationAnalyzerReceiptTests
{
    [Fact]
    public async Task SuppressedType_DuplicateIntKeys_ReportsMsgPack005WithoutAnalyzerCrash()
    {
        string source = """
            using MessagePack;

            namespace TestNs
            {
                [MessagePackObject(SuppressSourceGeneration = true)]
                public class DupKeys
                {
                    [Key(0)]
                    public int A { get; set; }

                    [Key(0)]
                    public int B { get; set; }
                }
            }
            """;

        (ImmutableArray<Diagnostic> diagnostics, List<Exception> exceptions) = await AnalyzeAsync(source);
        Assert.Empty(exceptions);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Contains(diagnostics, d => d.Id == "MsgPack005");
    }

    [Fact]
    public async Task SuppressedType_MixedKeyKinds_ReportsMsgPack005WithoutAnalyzerCrash()
    {
        string source = """
            using MessagePack;

            namespace TestNs
            {
                [MessagePackObject(SuppressSourceGeneration = true)]
                public class MixedKeys
                {
                    [Key(0)]
                    public int A { get; set; }

                    [Key("b")]
                    public int B { get; set; }
                }
            }
            """;

        (ImmutableArray<Diagnostic> diagnostics, List<Exception> exceptions) = await AnalyzeAsync(source);
        Assert.Empty(exceptions);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Contains(diagnostics, d => d.Id == "MsgPack005");
    }

    private static async Task<(ImmutableArray<Diagnostic> Diagnostics, List<Exception> Exceptions)> AnalyzeAsync(string source)
    {
        CSharpCompilation compilation = CreateCompilation(source);
        ImmutableArray<DiagnosticAnalyzer> analyzers = typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetCustomAttribute<DiagnosticAnalyzerAttribute>() is not null && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();
        Assert.NotEmpty(analyzers);

        var exceptions = new List<Exception>();
        var options = new CompilationWithAnalyzersOptions(
            new Microsoft.CodeAnalysis.Diagnostics.AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
            (ex, analyzer, diag) =>
            {
                lock (exceptions)
                {
                    exceptions.Add(ex);
                }
            },
            concurrentAnalysis: false,
            logAnalyzerExecutionTime: false);
        CompilationWithAnalyzers withAnalyzers = compilation.WithAnalyzers(analyzers, options);
        ImmutableArray<Diagnostic> diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();
        return (diagnostics, exceptions);
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        foreach (string path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string name = Path.GetFileName(path);
            if (name.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
                || name.Equals("netstandard.dll", StringComparison.OrdinalIgnoreCase)
                || name.Equals("mscorlib.dll", StringComparison.OrdinalIgnoreCase))
            {
                paths.Add(path);
            }
        }

        paths.Add(typeof(MessagePack.MessagePackObjectAttribute).Assembly.Location);
        paths.Add(typeof(MessagePack.MessagePackSerializer).Assembly.Location);

        IEnumerable<MetadataReference> references = paths.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp7_3));
        return CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
