// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack.SourceGenerator;
using MessagePack.SourceGenerator.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ClosedGenericStructFromMetadataTests(ITestOutputHelper logger)
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

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

    private static string GetLibSource(string variant) => variant switch
    {
        "RecordStruct" => """
            using MessagePack;
            namespace Lib;
            [MessagePackObject]
            public partial record struct Item<T>([property: Key(0)] string Key);
            """,
        "Struct" => """
            using MessagePack;
            namespace Lib;
            [MessagePackObject]
            public struct Item<T>
            {
                [Key(0)] public string Key { get; set; }
            }
            """,
        "StructWithUnkeyedMember" => """
            using MessagePack;
            namespace Lib;
            [MessagePackObject]
            public struct Item<T>
            {
                [Key(0)] public string Key { get; set; }
                public int Unkeyed { get; set; }
            }
            """,
        "StructWithoutMessagePackObject" => """
            namespace Lib;
            public struct Item<T>
            {
                public string Key { get; set; }
            }
            """,
        _ => throw new ArgumentOutOfRangeException(nameof(variant)),
    };

    [Theory]
    [InlineData("RecordStruct")]
    [InlineData("Struct")]
    [InlineData("StructWithUnkeyedMember")]
    [InlineData("StructWithoutMessagePackObject")]
    public async Task GeneratorAndAnalyzerDoNotCrash(string variant)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> baseRefs = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();

        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(GetLibSource(variant), ParseOptions) },
            baseRefs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var ms = new MemoryStream();
        var emitResult = lib.Emit(ms);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference libRef = MetadataReference.CreateFromImage(ms.ToArray());

        CSharpCompilation app = CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource, ParseOptions) },
            baseRefs.Concat(new[] { libRef }),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new MessagePackGenerator().AsSourceGenerator() }, parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(app, out _, out ImmutableArray<Diagnostic> generatorDiagnostics);
        GeneratorDriverRunResult runResult = driver.GetRunResult();
        foreach (var d in generatorDiagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        Assert.All(runResult.Results, r => Assert.Null(r.Exception));
        Assert.DoesNotContain(generatorDiagnostics, d => d.Id == "CS8785");

        var withAnalyzers = app.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new MsgPack00xMessagePackAnalyzer()));
        ImmutableArray<Diagnostic> all = await withAnalyzers.GetAllDiagnosticsAsync();
        foreach (var d in all)
        {
            logger.WriteLine(d.ToString());
        }

        Assert.DoesNotContain(all, d => d.Id == "AD0001");

        if (variant is "RecordStruct" or "Struct")
        {
            Assert.Contains(runResult.Results.SelectMany(r => r.GeneratedSources), s => s.HintName.Contains("AppResolver"));
        }
    }
}
