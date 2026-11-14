// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack.SourceGenerator;
using MessagePack.SourceGenerator.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ClosedGenericValueTypeFromReferenceTests
{
    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Latest);

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

    private static string LibSource(string kind) => kind switch
    {
        "record struct" => "using MessagePack; namespace Lib; [MessagePackObject] public partial record struct Item<T>([property: Key(0)] string Key);",
        "struct" => "using MessagePack; namespace Lib; [MessagePackObject] public partial struct Item<T> { [Key(0)] public string Key { get; set; } }",
        "class" => "using MessagePack; namespace Lib; [MessagePackObject] public partial class Item<T> { [Key(0)] public string Key { get; set; } }",
        _ => "using MessagePack; namespace Lib; [MessagePackObject] public partial record class Item<T>([property: Key(0)] string Key);",
    };

    private static async Task<CSharpCompilation> CreateAppCompilationAsync(string kind)
    {
        var framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> refs = Enumerable.Concat<MetadataReference>(framework, ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();

        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(LibSource(kind), ParseOptions) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var ms = new MemoryStream();
        var emitResult = lib.Emit(ms);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        refs.Add(MetadataReference.CreateFromImage(ms.ToArray()));
        return CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource, ParseOptions) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    [Theory]
    [InlineData("record struct")]
    [InlineData("struct")]
    [InlineData("class")]
    [InlineData("record class")]
    public async Task Generator_DoesNotThrow_AndEmitsResolver(string kind)
    {
        CSharpCompilation app = await CreateAppCompilationAsync(kind);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePackGenerator()).WithUpdatedParseOptions(ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(app, out _, out var generatorDiagnostics);
        GeneratorRunResult result = driver.GetRunResult().Results.Single();
        Assert.Null(result.Exception);
        Assert.DoesNotContain(generatorDiagnostics, d => d.Id == "CS8785");
        Assert.Contains(result.GeneratedSources, s => s.HintName.Contains("AppResolver"));
    }

    [Theory]
    [InlineData("record struct")]
    [InlineData("struct")]
    [InlineData("class")]
    [InlineData("record class")]
    public async Task Analyzer_DoesNotThrow(string kind)
    {
        CSharpCompilation app = await CreateAppCompilationAsync(kind);
        var withAnalyzers = app.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new MsgPack00xMessagePackAnalyzer()));
        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
    }
}
