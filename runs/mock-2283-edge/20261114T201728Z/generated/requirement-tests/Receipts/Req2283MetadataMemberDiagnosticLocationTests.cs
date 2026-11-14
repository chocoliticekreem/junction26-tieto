// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using MessagePack.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class Req2283MetadataMemberDiagnosticLocationTests(ITestOutputHelper logger)
{
    private const string LibSource = """
        using MessagePack;
        namespace Lib;

        [MessagePackObject]
        public struct Item<T>
        {
            [Key(null)]
            public string Name { get; set; }
        }
        """;

    private const string AppSource = """
        using MessagePack;
        using Lib;
        namespace App;

        [MessagePackObject]
        public sealed partial record Cmd([property: Key(0)] string Scope)
        {
            [Key(1)] public Item<int> Member { get; init; }
        }
        """;

    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Latest);

    [Fact]
    public async Task AnalyzerDoesNotThrowForNullKeyMemberOfReferencedGenericStruct()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = new List<MetadataReference>(framework);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        CSharpCompilation lib = CSharpCompilation.Create("Lib", new[] { CSharpSyntaxTree.ParseText(LibSource, ParseOptions) }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new MemoryStream();
        var emitResult = lib.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        references.Add(MetadataReference.CreateFromImage(stream.ToArray()));
        CSharpCompilation app = CSharpCompilation.Create("App", new[] { CSharpSyntaxTree.ParseText(AppSource, ParseOptions) }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        ImmutableArray<DiagnosticAnalyzer> analyzers = typeof(MessagePackGenerator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetCustomAttribute<DiagnosticAnalyzerAttribute>() is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t, nonPublic: true)!)
            .ToImmutableArray();
        Assert.NotEmpty(analyzers);

        ImmutableArray<Diagnostic> diagnostics = await app.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        foreach (Diagnostic d in diagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
    }
}
