// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ReferencedGenericValueTypeTests
{
    private static readonly CSharpParseOptions ParseOpts = new CSharpParseOptions(LanguageVersion.Latest);

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

    private static string LibSource(string kind) => kind == "record struct"
        ? """
            using MessagePack;
            namespace Lib;

            [MessagePackObject]
            public partial record struct Item<T>([property: Key(0)] string Key);
            """
        : """
            using MessagePack;
            namespace Lib;

            [MessagePackObject]
            public partial struct Item<T>
            {
                [Key(0)]
                public string Key { get; set; }
            }
            """;

    private static async Task<CSharpCompilation> CreateAppCompilationAsync(string kind)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> refs = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        CSharpCompilationOptions options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);

        CSharpCompilation lib = CSharpCompilation.Create("lib", new[] { CSharpSyntaxTree.ParseText(LibSource(kind), ParseOpts) }, refs, options);
        using MemoryStream ms = new MemoryStream();
        var emitResult = lib.Emit(ms);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics.Select(d => d.ToString())));
        MetadataReference libRef = MetadataReference.CreateFromImage(ms.ToArray());

        List<MetadataReference> appRefs = new List<MetadataReference>(refs) { libRef };
        return CSharpCompilation.Create("app", new[] { CSharpSyntaxTree.ParseText(AppSource, ParseOpts) }, appRefs, options);
    }

    [Theory]
    [InlineData("record struct")]
    [InlineData("struct")]
    public async Task Generator_DoesNotThrow_AndProducesOutput_ForClosedGenericValueTypeFromReference(string kind)
    {
        CSharpCompilation app = await CreateAppCompilationAsync(kind);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: ParseOpts);
        driver = driver.RunGeneratorsAndUpdateCompilation(app, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
        GeneratorDriverRunResult result = driver.GetRunResult();

        Assert.All(result.Results, r => Assert.Null(r.Exception));
        Assert.DoesNotContain(diagnostics, d => d.Id == "CS8785");
        Assert.NotEmpty(result.GeneratedTrees);
        Assert.Contains(result.GeneratedTrees, t => t.GetText().ToString().Contains("Cmd"));
    }

    [Theory]
    [InlineData("record struct")]
    [InlineData("struct")]
    public async Task Analyzers_DoNotThrow_ForClosedGenericValueTypeFromReference(string kind)
    {
        CSharpCompilation app = await CreateAppCompilationAsync(kind);

        ImmutableArray<DiagnosticAnalyzer> analyzers = typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();
        Assert.NotEmpty(analyzers);

        ImmutableArray<Diagnostic> diagnostics = await app.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();

        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
    }
}
