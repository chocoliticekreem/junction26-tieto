// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class SuppressSourceGenerationAnalyzerTests
{
    private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(string source)
    {
        ImmutableArray<MetadataReference> frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        IEnumerable<MetadataReference> refs = frameworkRefs.Concat<MetadataReference>(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        ImmutableArray<DiagnosticAnalyzer> analyzers = typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetCustomAttribute<DiagnosticAnalyzerAttribute>() is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();
        Assert.NotEmpty(analyzers);

        CompilationWithAnalyzers withAnalyzers = compilation.WithAnalyzers(analyzers);
        return await withAnalyzers.GetAllDiagnosticsAsync();
    }

    [Fact]
    public async Task MissingKeyIsReportedWhenSourceGenerationSuppressed()
    {
        string source = """
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class MyObject
            {
                [Key(0)]
                public int A { get; set; }

                public int B { get; set; }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Contains(diagnostics, d => d.Id == "MsgPack004");
    }

    [Fact]
    public async Task MixedKeysAreReportedWhenSourceGenerationSuppressed()
    {
        string source = """
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class MyObject
            {
                [Key(0)]
                public int A { get; set; }

                [Key("b")]
                public int B { get; set; }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await GetAnalyzerDiagnosticsAsync(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Contains(diagnostics, d => d.Id == "MsgPack005");
    }
}
