// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ClosedGenericMetadataValueTypeReceiptTests
{
    [Theory]
    [InlineData("public partial struct Item<T> { [Key(0)] public string Key { get; set; } }")]
    [InlineData("public partial record struct Item<T>([property: Key(0)] string Key);")]
    public async Task ClosedGenericValueTypesFromMetadata_DoNotCrashGeneratorOrAnalyzer(string itemDeclaration)
    {
        await AssertCleanGenerationAndAnalysisAsync(itemDeclaration);
    }

    [Fact]
    public async Task MetadataGenericStructMemberWithoutKey_DoesNotCrash()
    {
        await AssertCleanGenerationAndAnalysisAsync(
            "public partial struct Item<T> { public string Key { get; set; } }");
    }

    [Theory]
    [InlineData("public partial class Item<T> { [Key(0)] public string Key { get; set; } }", "Item<int>")]
    [InlineData("public partial record class Item<T>([property: Key(0)] string Key);", "Item<int>")]
    [InlineData("public partial struct Item { [Key(0)] public string Key { get; set; } }", "Item")]
    [InlineData("public partial record struct Item([property: Key(0)] string Key);", "Item")]
    public async Task OtherMetadataShapesFromTheIssue_StillWork(string itemDeclaration, string memberType)
    {
        await AssertCleanGenerationAndAnalysisAsync(itemDeclaration, memberType);
    }

    private static async Task AssertCleanGenerationAndAnalysisAsync(string itemDeclaration, string memberType = "Item<int>")
    {
        MetadataReference library = await CompileLibraryAsync(itemDeclaration);
        string appSource = $$"""
            using MessagePack;
            using Lib;

            namespace App;

            [MessagePackObject]
            public sealed partial record Cmd([property: Key(0)] string Scope)
            {
                [Key(1)] public {{memberType}} Member { get; init; }
            }

            [GeneratedMessagePackResolver]
            public partial class AppResolver
            {
            }
            """;

        IEnumerable<MetadataReference> references = await GetReferencesAsync();
        CSharpCompilation appCompilation = CreateCompilation(
            "ReceiptApp",
            appSource,
            references.Concat(new[] { library }));

        CompilationWithAnalyzers compilationWithAnalyzers = appCompilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(
                new MessagePack.SourceGenerator.Analyzers.MsgPack00xMessagePackAnalyzer()));
        ImmutableArray<Diagnostic> analyzerDiagnostics = await compilationWithAnalyzers.GetAllDiagnosticsAsync();
        Assert.DoesNotContain(analyzerDiagnostics, diagnostic => diagnostic.Id == "AD0001");

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: new CSharpParseOptions(LanguageVersion.Latest));
        driver = driver.RunGeneratorsAndUpdateCompilation(
            appCompilation,
            out _,
            out ImmutableArray<Diagnostic> generatorDiagnostics);

        GeneratorDriverRunResult runResult = driver.GetRunResult();
        ImmutableArray<Diagnostic> generationDiagnostics = generatorDiagnostics.AddRange(runResult.Diagnostics);
        Assert.DoesNotContain(generationDiagnostics, diagnostic => diagnostic.Id == "CS8785");

        string[] generatedHints = runResult.Results
            .SelectMany(result => result.GeneratedSources)
            .Select(source => source.HintName)
            .ToArray();
        Assert.Contains(
            generatedHints,
            hint => hint.EndsWith("AppResolver.g.cs", StringComparison.Ordinal));
    }

    private static async Task<MetadataReference> CompileLibraryAsync(string itemDeclaration)
    {
        string librarySource = $$"""
            using MessagePack;

            namespace Lib;

            [MessagePackObject]
            {{itemDeclaration}}
            """;
        CSharpCompilation compilation = CreateCompilation(
            "ReceiptLib",
            librarySource,
            await GetReferencesAsync());

        using MemoryStream stream = new();
        var emitResult = compilation.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static async Task<IEnumerable<MetadataReference>> GetReferencesAsync()
    {
        IEnumerable<MetadataReference> frameworkReferences =
            await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        return frameworkReferences.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
    }

    private static CSharpCompilation CreateCompilation(
        string assemblyName,
        string source,
        IEnumerable<MetadataReference> references)
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Latest));
        return CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
