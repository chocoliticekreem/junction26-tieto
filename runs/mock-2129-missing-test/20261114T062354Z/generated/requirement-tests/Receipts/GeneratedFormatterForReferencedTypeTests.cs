// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterForReferencedTypeTests
{
    private const string LibSource = """
        namespace ProjectA
        {
            public class MyObject
            {
                public int Id { get; set; }
                public string Label { get; set; }
                private int _hidden;
                public int GetHidden() => _hidden;
            }
        }
        """;

    private const string AppSource = """
        using MessagePack;
        using MessagePack.Formatters;

        [GeneratedMessagePackResolver]
        partial class MyResolver { }

        [GeneratedMessagePackFormatter]
        partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject>;
        """;

    [Fact]
    public async Task BodylessFormatterForReferencedType_CompilesWithoutErrors()
    {
        var (output, generatorDiagnostics, generated) = await RunAsync(LibSource, AppSource);

        Assert.DoesNotContain(generatorDiagnostics, d => d.Severity == DiagnosticSeverity.Error);
        var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        Assert.Contains(generated, s => s.Contains("MyObjectFormatter") && s.Contains("Serialize("));
    }

    [Fact]
    public async Task ContractlessFormatter_UsesPublicMembersOnly()
    {
        var (_, _, generated) = await RunAsync(LibSource, AppSource);

        string? formatter = generated.FirstOrDefault(s => s.Contains("MyObjectFormatter") && s.Contains("Serialize("));
        Assert.NotNull(formatter);
        Assert.Contains("Id", formatter);
        Assert.Contains("Label", formatter);
        Assert.DoesNotContain("_hidden", formatter);
    }

    private static async Task<(Compilation Output, ImmutableArray<Diagnostic> GeneratorDiagnostics, string[] Generated)> RunAsync(string libSource, string appSource)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        MetadataReference[] messagePack = ReferencesHelper.GetReferences(ReferencesSet.MessagePack).ToArray();
        CSharpParseOptions parse = new CSharpParseOptions(LanguageVersion.CSharp12);
        CSharpCompilationOptions compOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true);

        CSharpCompilation lib = CSharpCompilation.Create("ProjectA", new[] { CSharpSyntaxTree.ParseText(libSource, parse) }, framework, compOptions);
        using MemoryStream ms = new MemoryStream();
        var emit = lib.Emit(ms);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        MetadataReference libRef = MetadataReference.CreateFromImage(ms.ToArray());

        CSharpCompilation app = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(appSource, parse) },
            framework.Concat(messagePack).Append(libRef),
            compOptions);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new MessagePackGenerator().AsSourceGenerator() }, parseOptions: parse);
        driver = driver.RunGeneratorsAndUpdateCompilation(app, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
        string[] generated = driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()).ToArray();
        return (output, diagnostics, generated);
    }
}
