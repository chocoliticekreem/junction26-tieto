// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class PositionalRecordNullKeyDiagnosticTests
{
    private readonly ITestOutputHelper logger;

    public PositionalRecordNullKeyDiagnosticTests(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

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

        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        CSharpCompilation compilation = CSharpCompilation.Create(
            "app",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Assembly generatorAssembly = typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly;
        ImmutableArray<DiagnosticAnalyzer> analyzers = generatorAssembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetCustomAttributes(typeof(DiagnosticAnalyzerAttribute), false).Length > 0)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();

        Type analyzerType = generatorAssembly.GetTypes().Single(t => t.Name == "MsgPack00xMessagePackAnalyzer");
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        object? descriptorObj = analyzerType.GetField("BothStringAndIntKeyAreNull", flags)?.GetValue(null)
            ?? analyzerType.GetProperty("BothStringAndIntKeyAreNull", flags)?.GetValue(null);
        var descriptor = (DiagnosticDescriptor)descriptorObj!;

        ImmutableArray<Diagnostic> diagnostics = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        foreach (Diagnostic d in diagnostics)
        {
            this.logger.WriteLine(d.ToString());
        }

        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Contains(diagnostics, d => d.Descriptor.Equals(descriptor));
    }
}
