// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class Req2283ClosedGenericStructFromReferenceTests(ITestOutputHelper logger)
{
    private const string AppSource = """
        using MessagePack;
        using Lib;

        namespace App
        {
            [MessagePackObject]
            public sealed partial record Cmd([property: Key(0)] string Scope)
            {
                [Key(1)] public Item<int> Member { get; init; }
            }

            [GeneratedMessagePackResolver]
            partial class AppResolver
            {
            }
        }
        """;

    public static TheoryData<string> CrashingLibSources => new()
    {
        """
        using MessagePack;
        namespace Lib
        {
            [MessagePackObject]
            public partial record struct Item<T>([property: Key(0)] string Key);
        }
        """,
        """
        using MessagePack;
        namespace Lib
        {
            [MessagePackObject]
            public partial struct Item<T>
            {
                [Key(0)]
                public string Key { get; set; }
            }
        }
        """,
    };

    private static CSharpParseOptions ParseOptions => new CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest);

    [Theory]
    [MemberData(nameof(CrashingLibSources))]
    public async Task Generator_DoesNotThrowAndEmitsResolver_ForClosedGenericValueTypeFromReference(string libSource)
    {
        CSharpCompilation app = await CreateAppCompilationAsync(libSource);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(app, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        GeneratorDriverRunResult run = driver.GetRunResult();

        foreach (Diagnostic d in generatorDiagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        foreach (GeneratorRunResult result in run.Results)
        {
            Assert.Null(result.Exception);
        }

        Assert.DoesNotContain(generatorDiagnostics, d => d.Id == "CS8785");
        Assert.Contains(run.Results.SelectMany(r => r.GeneratedSources), s => s.HintName.Contains("AppResolver"));
    }

    [Theory]
    [MemberData(nameof(CrashingLibSources))]
    public async Task Analyzer_DoesNotThrowOnClosedGenericValueTypeFromReference(string libSource)
    {
        CSharpCompilation app = await CreateAppCompilationAsync(libSource);

        ImmutableArray<DiagnosticAnalyzer> analyzers = typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract
                && typeof(DiagnosticAnalyzer).IsAssignableFrom(t)
                && t.GetCustomAttribute<DiagnosticAnalyzerAttribute>() is not null
                && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();
        Assert.Contains(analyzers, a => a.GetType().Name == "MsgPack00xMessagePackAnalyzer");

        CompilationWithAnalyzers withAnalyzers = app.WithAnalyzers(analyzers);
        ImmutableArray<Diagnostic> diagnostics = await withAnalyzers.GetAllDiagnosticsAsync();

        foreach (Diagnostic d in diagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        Assert.Empty(diagnostics.Where(d => d.Id == "AD0001"));
    }

    private static async Task<CSharpCompilation> CreateAppCompilationAsync(string libSource)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = new List<MetadataReference>(framework);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(libSource, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitResult = lib.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics.Select(d => d.ToString())));

        // Reference the lib as a metadata image so its symbols have no declaring syntax.
        MetadataReference libReference = MetadataReference.CreateFromImage(stream.ToArray().ToImmutableArray());
        references.Add(libReference);

        return CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
