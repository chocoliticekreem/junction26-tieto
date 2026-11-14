// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using MessagePack.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class Req2283ReferencedGenericValueTypeTests(ITestOutputHelper logger)
{
    private const string RecordStructItem = """
        using MessagePack;
        namespace Lib;

        [MessagePackObject]
        public partial record struct Item<T>([property: Key(0)] string Key);
        """;

    private const string StructItem = """
        using MessagePack;
        namespace Lib;

        [MessagePackObject]
        public partial struct Item<T>
        {
            [Key(0)]
            public string Name { get; set; }
        }
        """;

    private const string ClassItem = """
        using MessagePack;
        namespace Lib;

        [MessagePackObject]
        public partial class Item<T>
        {
            [Key(0)]
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

        [GeneratedMessagePackResolver]
        partial class AppResolver { }
        """;

    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Latest);

    [Theory]
    [InlineData(RecordStructItem)]
    [InlineData(StructItem)]
    [InlineData(ClassItem)]
    public async Task GeneratorDoesNotThrowAndEmitsResolver(string libSource)
    {
        CSharpCompilation app = await CreateAppCompilationAsync(libSource);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new MessagePackGenerator().AsSourceGenerator() }, parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(app, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        foreach (Diagnostic d in generatorDiagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        GeneratorDriverRunResult runResult = driver.GetRunResult();
        foreach (GeneratorRunResult r in runResult.Results)
        {
            Assert.True(r.Exception is null, r.Exception?.ToString());
        }

        Assert.DoesNotContain(generatorDiagnostics, d => d.Id == "CS8785");
        Assert.Contains(runResult.GeneratedTrees, t => t.ToString().Contains("AppResolver"));
    }

    [Theory]
    [InlineData(RecordStructItem)]
    [InlineData(StructItem)]
    [InlineData(ClassItem)]
    public async Task AnalyzersDoNotThrow(string libSource)
    {
        CSharpCompilation app = await CreateAppCompilationAsync(libSource);
        ImmutableArray<DiagnosticAnalyzer> analyzers = GetAnalyzers();
        Assert.NotEmpty(analyzers);
        ImmutableArray<Diagnostic> diagnostics = await app.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        foreach (Diagnostic d in diagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
    }

    private static ImmutableArray<DiagnosticAnalyzer> GetAnalyzers() => typeof(MessagePackGenerator).Assembly.GetTypes()
        .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetCustomAttribute<DiagnosticAnalyzerAttribute>() is not null)
        .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t, nonPublic: true)!)
        .ToImmutableArray();

    private static async Task<CSharpCompilation> CreateAppCompilationAsync(string libSource)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = new List<MetadataReference>(framework);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        CSharpCompilation lib = CSharpCompilation.Create("Lib", new[] { CSharpSyntaxTree.ParseText(libSource, ParseOptions) }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new MemoryStream();
        var emitResult = lib.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        references.Add(MetadataReference.CreateFromImage(stream.ToArray()));
        return CSharpCompilation.Create("App", new[] { CSharpSyntaxTree.ParseText(AppSource, ParseOptions) }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
