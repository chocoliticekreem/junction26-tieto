// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class MetadataGenericStructDiagnosticLeakTests
{
    private readonly ITestOutputHelper logger;

    public MetadataGenericStructDiagnosticLeakTests(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    [Fact]
    public async Task NoMsgPackDiagnosticsForClosedGenericStructFromReferencedAssembly()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var refs = new List<MetadataReference>(framework);
        refs.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);

        string libSource = """
            using MessagePack;
            namespace Lib
            {
                [MessagePackObject]
                public struct Item<T>
                {
                    public int Value;
                }
            }
            """;

        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(libSource, parseOptions) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var emit = lib.Emit(ms);
        emit.Success.Should().BeTrue(string.Join(Environment.NewLine, emit.Diagnostics));
        MetadataReference libRef = MetadataReference.CreateFromImage(ms.ToArray());

        string appSource = """
            using MessagePack;
            using Lib;
            namespace App
            {
                [MessagePackObject]
                public class Cmd
                {
                    [Key(0)]
                    public Item<int> Member { get; set; }
                }
            }
            """;

        var appRefs = new List<MetadataReference>(refs) { libRef };
        CSharpCompilation app = CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(appSource, parseOptions) },
            appRefs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzers = typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly.GetTypes()
            .Where(t => typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();
        analyzers.Should().NotBeEmpty();

        ImmutableArray<Diagnostic> diagnostics = await app.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        foreach (Diagnostic d in diagnostics)
        {
            this.logger.WriteLine(d.ToString());
        }

        diagnostics.Where(d => d.Id.StartsWith("MsgPack", StringComparison.Ordinal) || d.Id == "AD0001")
            .Should().BeEmpty("types from a referenced assembly are not diagnosed by the consuming compilation");
    }
}
