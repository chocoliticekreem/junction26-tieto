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
    private const string DerivedKeySource = """
        using MessagePack;

        public class MyKeyAttribute : KeyAttribute
        {
            public MyKeyAttribute(int x) : base(x) { }
        }

        [MessagePackObject(SuppressSourceGeneration = true)]
        public class MyObject
        {
            [MyKey(0)]
            public int A { get; set; }

            [MyKey(1)]
            public int B { get; set; }

            public int Missing { get; set; }
        }
        """;

    [Fact]
    public async Task SuppressedTypeWithDerivedKeys_AnalyzerDoesNotThrow()
    {
        ImmutableArray<Diagnostic> diagnostics = await RunAnalyzersAsync(DerivedKeySource);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
    }

    [Fact]
    public async Task SuppressedTypeWithDerivedKeys_StillReportsMissingKey()
    {
        ImmutableArray<Diagnostic> diagnostics = await RunAnalyzersAsync(DerivedKeySource);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Contains(diagnostics, d => d.Id == "MsgPack004");
    }

    private static async Task<ImmutableArray<Diagnostic>> RunAnalyzersAsync(string source)
    {
        ImmutableArray<MetadataReference> frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        IEnumerable<MetadataReference> references = frameworkReferences.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        ImmutableArray<DiagnosticAnalyzer> analyzers = typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetCustomAttributes(typeof(DiagnosticAnalyzerAttribute), false).Length > 0)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();

        Assert.NotEmpty(analyzers);

        CompilationWithAnalyzers withAnalyzers = compilation.WithAnalyzers(analyzers);
        return await withAnalyzers.GetAnalyzerDiagnosticsAsync(CancellationToken.None);
    }
}
