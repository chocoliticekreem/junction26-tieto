// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class CharacterizeFormatterTemplateTests
{
    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Latest);

    private static async Task<(Compilation Output, GeneratorDriverRunResult Run)> RunGeneratorAsync(string source)
    {
        var frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var refs = frameworkRefs.Cast<MetadataReference>().Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack).Cast<MetadataReference>()).ToList();
        var compilation = CSharpCompilation.Create("TestAssembly", new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) }, refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ISourceGenerator[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() }, parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> _);
        GeneratorDriverRunResult run = driver.GetRunResult();
        Assert.All(run.Results, r => Assert.Null(r.Exception));
        Assert.DoesNotContain(run.Diagnostics, d => d.Id == "CS8785");
        return (output, run);
    }

    private static List<string> Sources(GeneratorDriverRunResult run)
    {
        return run.Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()).ToList();
    }

    private static void AssertNoErrors(Compilation output)
    {
        var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors.Select(e => e.ToString())));
    }

    [Fact]
    public async Task IntKeyClassWithGap_FormatterCompilesAndWritesMaxKeyPlusOne()
    {
        string source = """
            using MessagePack;

            namespace MyNs
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver { }
            }

            [MessagePackObject]
            public class GapObject
            {
                [Key(0)]
                public int First { get; set; }

                [Key(2)]
                public string Third { get; set; }
            }
            """;
        var (output, run) = await RunGeneratorAsync(source);
        AssertNoErrors(output);
        string formatter = Assert.Single(Sources(run), s => s.Contains("class GapObjectFormatter"));
        Assert.Contains("IMessagePackFormatter<", formatter);
        Assert.Contains("WriteArrayHeader(3)", formatter);
        Assert.Contains("WriteNil()", formatter);
        Assert.Contains("Deserialize", formatter);
    }

    [Fact]
    public async Task IntKeyStruct_FormatterCompilesAndRejectsNil()
    {
        string source = """
            using MessagePack;

            namespace MyNs
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver { }
            }

            [MessagePackObject]
            public struct Point
            {
                [Key(0)]
                public int X;

                [Key(1)]
                public int Y;
            }
            """;
        var (output, run) = await RunGeneratorAsync(source);
        AssertNoErrors(output);
        string formatter = Assert.Single(Sources(run), s => s.Contains("class PointFormatter"));
        Assert.Contains("InvalidOperationException", formatter);
        Assert.Contains("WriteArrayHeader(2)", formatter);
    }

    [Fact]
    public async Task CallbackReceiver_FormatterInvokesCallbacksAndCompiles()
    {
        string source = """
            using MessagePack;

            namespace MyNs
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver { }
            }

            [MessagePackObject]
            public class WithCallbacks : IMessagePackSerializationCallbackReceiver
            {
                [Key(0)]
                public int First { get; set; }

                [Key(1)]
                public string Second { get; set; }

                public void OnBeforeSerialize() { }

                public void OnAfterDeserialize() { }
            }
            """;
        var (output, run) = await RunGeneratorAsync(source);
        AssertNoErrors(output);
        string formatter = Assert.Single(Sources(run), s => s.Contains("class WithCallbacksFormatter"));
        Assert.Contains("OnBeforeSerialize()", formatter);
        Assert.Contains("OnAfterDeserialize()", formatter);
    }
}
