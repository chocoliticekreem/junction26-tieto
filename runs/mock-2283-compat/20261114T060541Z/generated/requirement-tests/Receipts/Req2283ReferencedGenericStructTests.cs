// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class Req2283ReferencedGenericStructTests
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    private const string RecordStructLibSource = """
        using MessagePack;
        namespace Lib;

        [MessagePackObject]
        public partial record struct Item<T>([property: Key(0)] string Key);
        """;

    private const string PlainStructLibSource = """
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
        using MessagePack; using Lib;
        namespace App;

        [MessagePackObject]
        public sealed partial record Cmd([property: Key(0)] string Scope)
        {
            [Key(1)] public Item<int> Member { get; init; }
        }

        [GeneratedMessagePackResolver]
        partial class MyResolver { }
        """;

    [Fact]
    public async Task Generator_DoesNotThrow_AndEmitsResolver_ForReferencedGenericRecordStruct()
    {
        CSharpCompilation app = await CreateAppCompilationAsync(RecordStructLibSource);
        AssertGeneratorSucceeds(app);
    }

    [Fact]
    public async Task Analyzer_DoesNotThrow_ForReferencedGenericRecordStruct()
    {
        CSharpCompilation app = await CreateAppCompilationAsync(RecordStructLibSource);
        await AssertAnalyzerDoesNotThrowAsync(app);
    }

    [Fact]
    public async Task GeneratorAndAnalyzer_DoNotThrow_ForReferencedGenericPlainStruct()
    {
        CSharpCompilation app = await CreateAppCompilationAsync(PlainStructLibSource);
        AssertGeneratorSucceeds(app);
        await AssertAnalyzerDoesNotThrowAsync(app);
    }

    private static void AssertGeneratorSucceeds(CSharpCompilation app)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(app, out Compilation _, out ImmutableArray<Diagnostic> driverDiagnostics);
        GeneratorDriverRunResult result = driver.GetRunResult();

        foreach (GeneratorRunResult r in result.Results)
        {
            Assert.True(r.Exception is null, "Generator threw: " + r.Exception);
        }

        Assert.DoesNotContain(driverDiagnostics, d => d.Id == "CS8785");
        Assert.Contains(result.GeneratedTrees, t => t.FilePath.Contains("MyResolver"));
    }

    private static async Task AssertAnalyzerDoesNotThrowAsync(CSharpCompilation app)
    {
        ImmutableArray<DiagnosticAnalyzer> analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new global::MessagePack.SourceGenerator.Analyzers.MsgPack00xMessagePackAnalyzer());
        ImmutableArray<Diagnostic> diagnostics = await app.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        Assert.True(!diagnostics.Any(d => d.Id == "AD0001"), string.Join(Environment.NewLine, diagnostics.Where(d => d.Id == "AD0001")));
    }

    private static async Task<CSharpCompilation> CreateAppCompilationAsync(string libSource)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        ImmutableArray<MetadataReference> references = framework.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(libSource, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emitResult = lib.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        // Emit to an image so that Item<T> is a metadata symbol (no DeclaringSyntaxReferences), as with a ProjectReference.
        MetadataReference libReference = MetadataReference.CreateFromImage(stream.ToArray());

        return CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource, ParseOptions) },
            references.Add(libReference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
