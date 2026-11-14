// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class Req2283ReferencedGenericStructTests(ITestOutputHelper logger)
{
    private const string LibStruct = """
        using MessagePack;
        namespace Lib
        {
            [MessagePackObject]
            public struct Item<T>
            {
                [Key(0)]
                public string Key { get; set; }
            }
        }
        """;

    private const string LibRecordStruct = """
        using MessagePack;
        namespace Lib
        {
            [MessagePackObject]
            public partial record struct Item<T>([property: Key(0)] string Key);
        }
        """;

    private const string AppClass = """
        using MessagePack;
        using Lib;
        namespace App
        {
            [GeneratedMessagePackResolver]
            partial class MyResolver { }

            [MessagePackObject]
            public class Cmd
            {
                [Key(0)]
                public string Scope { get; set; }

                [Key(1)]
                public Item<int> Member { get; set; }
            }
        }
        """;

    private const string AppRecord = """
        using MessagePack;
        using Lib;
        namespace App
        {
            [GeneratedMessagePackResolver]
            partial class MyResolver { }

            [MessagePackObject]
            public sealed partial record Cmd([property: Key(0)] string Scope)
            {
                [Key(1)] public Item<int> Member { get; init; }
            }
        }
        """;

    [Fact]
    public Task GenericStructFromReferencedAssembly_DoesNotCrash() => this.RunAsync(LibStruct, AppClass);

    [Fact]
    public Task GenericRecordStructFromReferencedAssembly_DoesNotCrash() => this.RunAsync(LibRecordStruct, AppRecord);

    private async Task RunAsync(string libSource, string appSource)
    {
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        CSharpCompilationOptions compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = Enumerable.Concat<MetadataReference>(framework, ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();

        CSharpCompilation lib = CSharpCompilation.Create("Lib", new[] { CSharpSyntaxTree.ParseText(libSource, parseOptions) }, references, compilationOptions);
        using MemoryStream stream = new MemoryStream();
        var emitResult = lib.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics.Select(d => d.ToString())));
        MetadataReference libReference = MetadataReference.CreateFromImage(stream.ToArray());

        List<MetadataReference> appReferences = new List<MetadataReference>(references) { libReference };
        CSharpCompilation app = CSharpCompilation.Create("App", new[] { CSharpSyntaxTree.ParseText(appSource, parseOptions) }, appReferences, compilationOptions);

        // Generator
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() }, parseOptions: parseOptions);
        driver = driver.RunGenerators(app);
        GeneratorDriverRunResult run = driver.GetRunResult();
        foreach (GeneratorRunResult result in run.Results)
        {
            if (result.Exception is not null)
            {
                logger.WriteLine(result.Exception.ToString());
            }

            Assert.Null(result.Exception);
        }

        Assert.DoesNotContain(run.Diagnostics, d => d.Id == "CS8785");
        Assert.Contains(run.GeneratedTrees, t => t.FilePath.Contains("MyResolver"));

        // Analyzer
        Type analyzerType = typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly.GetType("MessagePack.SourceGenerator.Analyzers.MsgPack00xMessagePackAnalyzer", throwOnError: true)!;
        DiagnosticAnalyzer analyzer = (DiagnosticAnalyzer)Activator.CreateInstance(analyzerType, nonPublic: true)!;
        List<Exception> exceptions = new List<Exception>();
        CompilationWithAnalyzersOptions analyzerOptions = new CompilationWithAnalyzersOptions(
            new Microsoft.CodeAnalysis.Diagnostics.AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
            (ex, a, d) =>
            {
                lock (exceptions)
                {
                    exceptions.Add(ex);
                }
            },
            concurrentAnalysis: false,
            logAnalyzerExecutionTime: false);
        CompilationWithAnalyzers withAnalyzers = app.WithAnalyzers(ImmutableArray.Create(analyzer), analyzerOptions);
        ImmutableArray<Diagnostic> analyzerDiagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync(CancellationToken.None);
        foreach (Exception ex in exceptions)
        {
            logger.WriteLine(ex.ToString());
        }

        Assert.Empty(exceptions);
        Assert.DoesNotContain(analyzerDiagnostics, d => d.Id == "AD0001");
    }
}
