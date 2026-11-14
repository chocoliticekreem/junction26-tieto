// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class ClosedGenericStructFromReferencedAssemblyTests
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
        """;

    [Theory]
    [InlineData("recordstruct")]
    [InlineData("struct")]
    public async Task AppBuildsWithoutGeneratorOrAnalyzerCrash(string kind)
    {
        MetadataReference lib = await BuildLibAsync(kind == "struct" ? StructLib : RecordStructLib);
        var test = new VerifyCS.Test
        {
            LanguageVersion = LanguageVersion.Latest,
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
        };
        test.TestState.Sources.Add(AppSource);
        test.TestState.AdditionalReferences.Add(lib);
        await test.RunAsync();
    }

    [Fact]
    public async Task GeneratorDoesNotThrowAndEmitsSource()
    {
        MetadataReference lib = await BuildLibAsync(RecordStructLib);
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        List<MetadataReference> references = await GetBaseReferencesAsync();
        references.Add(lib);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator()).WithUpdatedParseOptions(parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        GeneratorDriverRunResult result = driver.GetRunResult();

        foreach (GeneratorRunResult r in result.Results)
        {
            Xunit.Assert.Null(r.Exception);
        }

        Xunit.Assert.DoesNotContain(generatorDiagnostics, d => d.Id == "CS8785");
        Xunit.Assert.NotEmpty(result.GeneratedTrees);
        Xunit.Assert.DoesNotContain(output.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
    }

    private static async Task<List<MetadataReference>> GetBaseReferencesAsync()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        return framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack).Cast<MetadataReference>()).ToList();
    }

    private static async Task<MetadataReference> BuildLibAsync(string source)
    {
        List<MetadataReference> references = await GetBaseReferencesAsync();
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        Xunit.Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }
}
