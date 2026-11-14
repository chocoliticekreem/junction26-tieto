// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class Req2283ClosedGenericStructFromReferenceTests
{
    private const string LibSource = """
        using MessagePack;
        namespace Lib;

        [MessagePackObject]
        public partial record struct Item<T>([property: Key(0)] string Key);
        """;

    private const string AppSource = """
        using MessagePack; using Lib;
        namespace App;

        [MessagePackObject]
        public sealed partial record Cmd([property: Key(0)] string Scope)
        {
            [Key(1)] public Item<int> Member { get; init; }
        }
        """;

    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.CSharp12);

    [Fact]
    public async Task GeneratorDoesNotThrowAndEmitsResolverForClosedGenericRecordStructFromReference()
    {
        Compilation app = await CreateAppCompilationAsync();

        var generator = new MessagePack.SourceGenerator.MessagePackGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { generator.AsSourceGenerator() }, parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(app, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
        GeneratorDriverRunResult run = driver.GetRunResult();

        run.Results.Should().ContainSingle();
        run.Results[0].Exception.Should().BeNull();
        diagnostics.Should().NotContain(d => d.Id == "CS8785");
        run.GeneratedTrees.Should().Contain(t => t.GetText(CancellationToken.None).ToString().Contains("GeneratedMessagePackResolver"));
    }

    [Fact]
    public async Task AnalyzerDoesNotThrowOnClosedGenericRecordStructFromReference()
    {
        Compilation app = await CreateAppCompilationAsync();

        ImmutableArray<DiagnosticAnalyzer> analyzers = typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract
                && typeof(DiagnosticAnalyzer).IsAssignableFrom(t)
                && t.GetCustomAttributes(typeof(DiagnosticAnalyzerAttribute), false).Length > 0
                && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();
        analyzers.Should().NotBeEmpty();

        var exceptions = new List<Exception>();
        var options = new CompilationWithAnalyzersOptions(
            new Microsoft.CodeAnalysis.Diagnostics.AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
            (ex, analyzer, diag) => { lock (exceptions) { exceptions.Add(ex); } },
            concurrentAnalysis: false,
            logAnalyzerExecutionTime: false);
        var withAnalyzers = new CompilationWithAnalyzers(app, analyzers, options);
        ImmutableArray<Diagnostic> diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync(CancellationToken.None);

        exceptions.Should().BeEmpty();
        diagnostics.Should().NotContain(d => d.Id == "AD0001");
    }

    private static async Task<Compilation> CreateAppCompilationAsync()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        var compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);

        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(LibSource, ParseOptions) },
            references,
            compilationOptions);
        using var stream = new MemoryStream();
        var emitResult = lib.Emit(stream);
        emitResult.Success.Should().BeTrue(string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference libReference = MetadataReference.CreateFromImage(stream.ToArray());

        var appReferences = new List<MetadataReference>(references) { libReference };
        return CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource, ParseOptions) },
            appReferences,
            compilationOptions);
    }
}
