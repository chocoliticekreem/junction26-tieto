// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ClosedGenericStructFromReferencedAssemblyTests
{
    private const string AppSource = """
        using MessagePack;
        using Lib;

        namespace App;

        [MessagePackObject]
        public sealed partial record Cmd([property: Key(0)] string Scope)
        {
            [Key(1)] public Item<int> Member { get; init; }
        }

        [GeneratedMessagePackResolver]
        internal partial class AppResolver
        {
        }
        """;

    [Theory]
    [InlineData("public partial record struct Item<T>([property: Key(0)] string Key);")]
    [InlineData("public partial struct Item<T> { [Key(0)] public string Key { get; set; } }")]
    public async Task Generator_DoesNotThrow_AndEmitsResolver(string itemDeclaration)
    {
        (CSharpCompilation app, CSharpParseOptions parseOptions) = await CreateAppCompilationAsync(itemDeclaration);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(app, out _, out ImmutableArray<Diagnostic> generatorDiagnostics);
        GeneratorDriverRunResult result = driver.GetRunResult();

        Assert.All(result.Results, r => Assert.Null(r.Exception));
        Assert.DoesNotContain(generatorDiagnostics, d => d.Id == "CS8785");
        Assert.Contains(result.Results.SelectMany(r => r.GeneratedSources), s => s.SourceText.ToString().Contains("AppResolver"));
    }

    [Theory]
    [InlineData("public partial record struct Item<T>([property: Key(0)] string Key);")]
    [InlineData("public partial struct Item<T> { [Key(0)] public string Key { get; set; } }")]
    public async Task Analyzer_DoesNotThrow(string itemDeclaration)
    {
        (CSharpCompilation app, _) = await CreateAppCompilationAsync(itemDeclaration);

        List<Exception> exceptions = new();
        CompilationWithAnalyzersOptions options = new(
            new Microsoft.CodeAnalysis.Diagnostics.AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
            (ex, analyzer, diag) =>
            {
                lock (exceptions)
                {
                    exceptions.Add(ex);
                }
            },
            false,
            false);

        CompilationWithAnalyzers withAnalyzers = app.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new global::MessagePack.SourceGenerator.Analyzers.MsgPack00xMessagePackAnalyzer()),
            options);
        ImmutableArray<Diagnostic> diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync(CancellationToken.None);

        Assert.Empty(exceptions);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
    }

    private static async Task<(CSharpCompilation App, CSharpParseOptions ParseOptions)> CreateAppCompilationAsync(string itemDeclaration)
    {
        CSharpParseOptions parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);

        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = new(framework);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        string libSource = $$"""
            using MessagePack;

            namespace Lib;

            [MessagePackObject]
            {{itemDeclaration}}
            """;

        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(libSource, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using MemoryStream stream = new();
        var emitResult = lib.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        // A PE image reference, as a ProjectReference produces: symbols from it have no declaring syntax.
        MetadataReference libReference = MetadataReference.CreateFromImage(stream.ToArray());

        List<MetadataReference> appReferences = new(references) { libReference };
        CSharpCompilation app = CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource, parseOptions) },
            appReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return (app, parseOptions);
    }
}
