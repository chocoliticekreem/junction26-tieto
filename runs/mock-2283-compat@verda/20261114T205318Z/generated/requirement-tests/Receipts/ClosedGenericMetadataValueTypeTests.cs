// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack.SourceGenerator.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ClosedGenericMetadataValueTypeTests
{
    [Theory]
    [InlineData("public partial struct Item<T> { [Key(0)] public string Key { get; set; } }")]
    [InlineData("public partial record struct Item<T>([property: Key(0)] string Key);")]
    public async Task ClosedGenericValueTypeFromReferencedAssembly_DoesNotCrashAndGeneratesResolver(string itemDeclaration)
    {
        await RunCaseAsync(itemDeclaration, "Item<int>");
    }

    [Theory]
    [InlineData("public partial class Item<T> { [Key(0)] public string Key { get; set; } }", "Item<int>")]
    [InlineData("public partial struct Item { [Key(0)] public string Key { get; set; } }", "Item")]
    public async Task OtherMetadataTypeShapes_StillGenerateResolver(string itemDeclaration, string memberType)
    {
        await RunCaseAsync(itemDeclaration, memberType);
    }

    private static async Task RunCaseAsync(string itemDeclaration, string memberType)
    {
        MetadataReference libraryReference = await CompileLibraryAsync(itemDeclaration);
        string appSource = $$"""
            using MessagePack;
            using Lib;

            namespace App;

            [MessagePackObject]
            public sealed partial record Cmd([property: Key(0)] string Scope)
            {
                [Key(1)]
                public {{memberType}} Member { get; init; }
            }

            [GeneratedMessagePackResolver]
            public partial class TestResolver
            {
            }
            """;

        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        List<MetadataReference> references = await GetFrameworkAndMessagePackReferencesAsync();
        references.Add(libraryReference);

        CSharpCompilation compilation = CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(appSource, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        ISourceGenerator generator = new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out _,
            out ImmutableArray<Diagnostic> generatorDiagnostics,
            CancellationToken.None);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Id == "CS8785");
        GeneratorDriverRunResult runResult = driver.GetRunResult();
        Assert.DoesNotContain(runResult.Diagnostics, diagnostic => diagnostic.Id == "CS8785");
        Assert.Contains(
            runResult.GeneratedTrees,
            tree => tree.FilePath.EndsWith("App.TestResolver.g.cs", StringComparison.Ordinal));

        ImmutableArray<Diagnostic> analyzerDiagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new MsgPack00xMessagePackAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(analyzerDiagnostics, diagnostic => diagnostic.Id == "AD0001");
    }

    private static async Task<MetadataReference> CompileLibraryAsync(string itemDeclaration)
    {
        string libSource = $$"""
            using MessagePack;

            namespace Lib;

            [MessagePackObject]
            {{itemDeclaration}}
            """;

        List<MetadataReference> references = await GetFrameworkAndMessagePackReferencesAsync();
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(libSource, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitResult = compilation.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static async Task<List<MetadataReference>> GetFrameworkAndMessagePackReferencesAsync()
    {
        var frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = frameworkReferences.ToList();
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        return references;
    }
}
