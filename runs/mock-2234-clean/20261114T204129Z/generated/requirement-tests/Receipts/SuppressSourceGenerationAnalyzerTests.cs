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
    private readonly ITestOutputHelper logger;

    public SuppressSourceGenerationAnalyzerTests(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    [Fact]
    public async Task DuplicateIntKeys_OnSuppressedType_ReportsMsgPack005()
    {
        string source = """
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class DuplicateIntKeys
            {
                [Key(0)]
                public int A { get; set; }

                [Key(0)]
                public int B { get; set; }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await this.GetAnalyzerDiagnosticsAsync(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Contains(diagnostics, d => d.Id == "MsgPack005");
    }

    [Fact]
    public async Task DuplicateStringKeys_OnSuppressedType_ReportsMsgPack005()
    {
        string source = """
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class DuplicateStringKeys
            {
                [Key("a")]
                public int A { get; set; }

                [Key("a")]
                public int B { get; set; }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await this.GetAnalyzerDiagnosticsAsync(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Contains(diagnostics, d => d.Id == "MsgPack005");
    }

    [Fact]
    public async Task MixedKeys_OnSuppressedType_ReportsMsgPack005()
    {
        string source = """
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class MixedKeys
            {
                [Key(0)]
                public int A { get; set; }

                [Key("b")]
                public int B { get; set; }
            }
            """;

        ImmutableArray<Diagnostic> diagnostics = await this.GetAnalyzerDiagnosticsAsync(source);

        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Contains(diagnostics, d => d.Id == "MsgPack005");
    }

    private static ImmutableArray<DiagnosticAnalyzer> LoadAnalyzers()
    {
        Assembly assembly = typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly;
        Type?[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types;
        }

        return types
            .Where(t => t is not null && !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t!)!)
            .ToImmutableArray();
    }

    private async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(string source)
    {
        ImmutableArray<MetadataReference> frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = new List<MetadataReference>(frameworkReferences);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsSuppressSourceGenerationAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        ImmutableArray<DiagnosticAnalyzer> analyzers = LoadAnalyzers();
        Assert.NotEmpty(analyzers);

        ImmutableArray<Diagnostic> diagnostics = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        foreach (Diagnostic diagnostic in diagnostics)
        {
            this.logger.WriteLine(diagnostic.ToString());
        }

        return diagnostics;
    }
}
