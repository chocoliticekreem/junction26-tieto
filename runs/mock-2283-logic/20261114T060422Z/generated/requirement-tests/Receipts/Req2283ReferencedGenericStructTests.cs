// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using MessagePack.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class Req2283ReferencedGenericStructTests(ITestOutputHelper logger)
{
    private const string RecordStructLib = """
        using MessagePack;

        namespace Lib;

        [MessagePackObject]
        public partial record struct Item<T>([property: Key(0)] string Key);
        """;

    private const string StructLib = """
        using MessagePack;

        namespace Lib;

        [MessagePackObject]
        public partial struct Item<T>
        {
            [Key(0)]
            public string Key { get; set; }
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

        [GeneratedMessagePackResolver]
        partial class AppResolver { }
        """;

    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Latest);

    [Theory]
    [InlineData(RecordStructLib)]
    [InlineData(StructLib)]
    public async Task Generator_DoesNotThrow_AndEmitsResolver(string libSource)
    {
        CSharpCompilation app = await CreateAppCompilationAsync(libSource);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePackGenerator()).WithUpdatedParseOptions(ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(app, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        GeneratorDriverRunResult result = driver.GetRunResult();
        foreach (Diagnostic d in generatorDiagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        Assert.DoesNotContain(generatorDiagnostics, d => d.Id == "CS8785");
        Assert.All(result.Results, r => Assert.Null(r.Exception));
        Assert.Contains(result.GeneratedTrees, t => t.GetText().ToString().Contains("AppResolver"));
    }

    [Theory]
    [InlineData(RecordStructLib)]
    [InlineData(StructLib)]
    public async Task Analyzer_DoesNotThrow(string libSource)
    {
        CSharpCompilation app = await CreateAppCompilationAsync(libSource);
        ImmutableArray<DiagnosticAnalyzer> analyzers = typeof(MessagePackGenerator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract
                && typeof(DiagnosticAnalyzer).IsAssignableFrom(t)
                && t.GetCustomAttributes(typeof(DiagnosticAnalyzerAttribute), false).Length > 0
                && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();
        Assert.NotEmpty(analyzers);

        ImmutableArray<Diagnostic> diagnostics = await app.WithAnalyzers(analyzers).GetAllDiagnosticsAsync();
        foreach (Diagnostic d in diagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
    }

    private static async Task<CSharpCompilation> CreateAppCompilationAsync(string libSource)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        MetadataReference[] baseReferences = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToArray();
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);

        CSharpCompilation lib = CSharpCompilation.Create("Lib", new[] { CSharpSyntaxTree.ParseText(libSource, ParseOptions) }, baseReferences, options);
        using var stream = new MemoryStream();
        var emitResult = lib.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference libReference = MetadataReference.CreateFromImage(stream.ToArray());

        return CSharpCompilation.Create("App", new[] { CSharpSyntaxTree.ParseText(AppSource, ParseOptions) }, baseReferences.Append(libReference), options);
    }
}
