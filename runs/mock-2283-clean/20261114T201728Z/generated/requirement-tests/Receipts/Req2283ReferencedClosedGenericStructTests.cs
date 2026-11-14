// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class Req2283ReferencedClosedGenericStructTests(ITestOutputHelper logger)
{
    private const string AppSource = """
        using MessagePack;
        using Lib;

        namespace App;

        [GeneratedMessagePackResolver]
        partial class AppResolver
        {
        }

        [MessagePackObject]
        public sealed partial record Cmd([property: Key(0)] string Scope)
        {
            [Key(1)]
            public Item<int> Member { get; init; }
        }
        """;

    private const string RecordStructLibSource = """
        using MessagePack;

        namespace Lib;

        [MessagePackObject]
        public partial record struct Item<T>([property: Key(0)] string Key);
        """;

    private const string StructLibSource = """
        using MessagePack;

        namespace Lib;

        [MessagePackObject]
        public partial struct Item<T>
        {
            [Key(0)]
            public string Key { get; set; }
        }
        """;

    private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Generator_DoesNotThrow_AndEmitsResolver(bool recordStruct)
    {
        CSharpCompilation app = await this.CreateAppCompilationAsync(recordStruct);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePackGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(app, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        GeneratorDriverRunResult runResult = driver.GetRunResult();

        foreach (Diagnostic d in generatorDiagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        foreach (GeneratorRunResult r in runResult.Results)
        {
            if (r.Exception is not null)
            {
                logger.WriteLine(r.Exception.ToString());
            }
        }

        Assert.All(runResult.Results, r => Assert.Null(r.Exception));
        Assert.DoesNotContain(generatorDiagnostics, d => d.Id == "CS8785");
        Assert.Contains(runResult.GeneratedTrees, t => t.FilePath.Contains("AppResolver", StringComparison.Ordinal));

        Diagnostic[] errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        foreach (Diagnostic e in errors)
        {
            logger.WriteLine(e.ToString());
        }

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Analyzer_DoesNotThrow(bool recordStruct)
    {
        CSharpCompilation app = await this.CreateAppCompilationAsync(recordStruct);

        ImmutableArray<DiagnosticAnalyzer> analyzers = typeof(MessagePackGenerator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.IsDefined(typeof(DiagnosticAnalyzerAttribute), false))
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();
        Assert.NotEmpty(analyzers);

        ImmutableArray<Diagnostic> diagnostics = await app.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        foreach (Diagnostic d in diagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
    }

    private static async Task<ImmutableArray<MetadataReference>> GetReferencesAsync()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        return framework.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
    }

    private async Task<CSharpCompilation> CreateAppCompilationAsync(bool recordStruct)
    {
        ImmutableArray<MetadataReference> references = await GetReferencesAsync();

        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(recordStruct ? RecordStructLibSource : StructLibSource, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using MemoryStream stream = new();
        var emitResult = lib.Emit(stream);
        foreach (Diagnostic d in emitResult.Diagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        Assert.True(emitResult.Success, "The referenced 'Lib' assembly failed to compile.");
        MetadataReference libReference = MetadataReference.CreateFromImage(stream.ToArray());

        return CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource, ParseOptions) },
            references.Add(libReference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
