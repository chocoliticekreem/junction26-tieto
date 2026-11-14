// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class Req2283ReferencedGenericStructTests
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
        public struct Item<T>
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
        internal partial class AppResolver
        {
        }
        """;

    private static readonly CSharpParseOptions LangOptions = new(LanguageVersion.CSharp12);

    private readonly ITestOutputHelper logger;

    public Req2283ReferencedGenericStructTests(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Generator_DoesNotThrow_OnClosedGenericStructFromReferencedAssembly(bool recordStruct)
    {
        CSharpCompilation app = await CreateAppCompilationAsync(recordStruct ? RecordStructLib : StructLib);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: LangOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(app, out _, out ImmutableArray<Diagnostic> generatorDiagnostics);
        GeneratorDriverRunResult runResult = driver.GetRunResult();

        foreach (Diagnostic d in generatorDiagnostics)
        {
            this.logger.WriteLine(d.ToString());
        }

        Assert.DoesNotContain(generatorDiagnostics, d => d.Id == "CS8785");
        Assert.All(runResult.Results, r => Assert.Null(r.Exception));
        Assert.Contains(runResult.Results.SelectMany(r => r.GeneratedSources), s => s.SourceText.ToString().Contains("Cmd"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Analyzers_DoNotThrow_OnClosedGenericStructFromReferencedAssembly(bool recordStruct)
    {
        CSharpCompilation app = await CreateAppCompilationAsync(recordStruct ? RecordStructLib : StructLib);

        ImmutableArray<DiagnosticAnalyzer> analyzers = GetLoadableTypes(typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly)
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetCustomAttributes(typeof(DiagnosticAnalyzerAttribute), false).Length > 0)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t, nonPublic: true)!)
            .ToImmutableArray();
        Assert.Contains(analyzers, a => a.GetType().Name == "MsgPack00xMessagePackAnalyzer");

        ImmutableArray<Diagnostic> diagnostics = await app.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        foreach (Diagnostic d in diagnostics)
        {
            this.logger.WriteLine(d.ToString());
        }

        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null).Select(t => t!);
        }
    }

    private static async Task<CSharpCompilation> CreateAppCompilationAsync(string libSource)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = new(framework);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        CSharpCompilationOptions options = new(OutputKind.DynamicallyLinkedLibrary);

        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(libSource, LangOptions) },
            references,
            options);
        using MemoryStream stream = new();
        var emitResult = lib.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        references.Add(MetadataReference.CreateFromImage(stream.ToArray()));

        return CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource, LangOptions) },
            references,
            options);
    }
}
