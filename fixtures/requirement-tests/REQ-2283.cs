// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

// Requirement test for REQ-2283, written from requirements/REQ-2283.md only:
// the generator and MsgPack00x analyzer must not throw when a [MessagePackObject] type has a member
// whose type is a closed generic value type declared in a referenced assembly, and the consuming
// assembly must still get its generated resolver.
using MessagePack.SourceGenerator;
using MessagePack.SourceGenerator.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

public class Req2283ClosedGenericFromReferencedAssemblyTests
{
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

    [Fact]
    public Task GenericRecordStructFromReferencedAssembly_DoesNotThrow()
        => AssertCleanAsync("""
            using MessagePack;
            namespace Lib;

            [MessagePackObject]
            public partial record struct Item<T>([property: Key(0)] string Key);
            """);

    [Fact]
    public Task GenericStructFromReferencedAssembly_DoesNotThrow()
        => AssertCleanAsync("""
            using MessagePack;
            namespace Lib;

            [MessagePackObject]
            public partial struct Item<T>
            {
                [Key(0)] public string Key { get; set; }
            }
            """);

    [Fact]
    public Task GenericClassFromReferencedAssembly_StillWorks()
        => AssertCleanAsync("""
            using MessagePack;
            namespace Lib;

            [MessagePackObject]
            public partial record class Item<T>([property: Key(0)] string Key);
            """);

    private static async Task AssertCleanAsync(string libSource)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        var compileOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);

        CSharpCompilation lib = CSharpCompilation.Create("Lib", new[] { CSharpSyntaxTree.ParseText(libSource, parseOptions) }, references, compileOptions);
        using var image = new MemoryStream();
        var emit = lib.Emit(image);
        Assert.True(emit.Success, "lib must compile: " + string.Join(Environment.NewLine, emit.Diagnostics));
        references.Add(MetadataReference.CreateFromImage(image.ToArray()));

        CSharpCompilation app = CSharpCompilation.Create("App", new[] { CSharpSyntaxTree.ParseText(AppSource, parseOptions) }, references, compileOptions);

        GeneratorDriverRunResult run = CSharpGeneratorDriver.Create(new[] { new MessagePackGenerator().AsSourceGenerator() }, parseOptions: parseOptions)
            .RunGenerators(app)
            .GetRunResult();
        GeneratorRunResult generator = Assert.Single(run.Results);
        Assert.Null(generator.Exception);
        Assert.DoesNotContain(run.Diagnostics, d => d.Id == "CS8785");
        Assert.Contains(generator.GeneratedSources, s => s.HintName.Contains("AppResolver"));

        var analyzerExceptions = new List<Exception>();
        var analyzerOptions = new CompilationWithAnalyzersOptions(
            new Microsoft.CodeAnalysis.Diagnostics.AnalyzerOptions(ImmutableArray<AdditionalText>.Empty),
            (ex, analyzer, diagnostic) => analyzerExceptions.Add(ex),
            concurrentAnalysis: false,
            logAnalyzerExecutionTime: false);
        ImmutableArray<Diagnostic> analyzerDiagnostics = await app
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new MsgPack00xMessagePackAnalyzer()), analyzerOptions)
            .GetAnalyzerDiagnosticsAsync();
        Assert.Empty(analyzerExceptions);
        Assert.DoesNotContain(analyzerDiagnostics, d => d.Id == "AD0001");
    }
}
