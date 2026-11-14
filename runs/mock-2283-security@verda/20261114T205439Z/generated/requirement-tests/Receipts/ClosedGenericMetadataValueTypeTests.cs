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
    [InlineData("public partial record struct Item<T>([property: Key(0)] string Key);", "Item<int>")]
    [InlineData("public partial struct Item<T> { [Key(0)] public string Key { get; set; } = string.Empty; }", "Item<int>")]
    public async Task ClosedGenericValueTypeFromReferencedAssembly_GeneratesResolverWithoutCrashes(string itemDeclaration, string memberType)
    {
        await RunCrossAssemblyScenarioAsync(itemDeclaration, memberType);
    }

    [Theory]
    [InlineData("public partial class Item<T> { [Key(0)] public string Key { get; set; } = string.Empty; }", "Item<int>")]
    [InlineData("public partial record class Item<T>([property: Key(0)] string Key);", "Item<int>")]
    [InlineData("public partial struct Item { [Key(0)] public string Key { get; set; } = string.Empty; }", "Item")]
    [InlineData("public partial record struct Item([property: Key(0)] string Key);", "Item")]
    public async Task PreviouslyWorkingMetadataTypeShapesRemainSupported(string itemDeclaration, string memberType)
    {
        await RunCrossAssemblyScenarioAsync(itemDeclaration, memberType);
    }

    private static async Task RunCrossAssemblyScenarioAsync(string itemDeclaration, string memberType)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        MetadataReference[] frameworkReferences = (await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None)).ToArray();
        MetadataReference[] messagePackReferences = ReferencesHelper.GetReferences(ReferencesSet.MessagePack).ToArray();

        string librarySource = $$"""
            using MessagePack;
            namespace Lib;

            [MessagePackObject]
            {{itemDeclaration}}
            """;

        CSharpCompilation libraryCompilation = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(librarySource, parseOptions) },
            frameworkReferences.Concat(messagePackReferences),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(libraryCompilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        using var libraryImage = new MemoryStream();
        var emitResult = libraryCompilation.Emit(libraryImage);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference libraryReference = MetadataReference.CreateFromImage(libraryImage.ToArray());

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

        CSharpCompilation appCompilation = CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(appSource, parseOptions) },
            frameworkReferences.Concat(messagePackReferences).Append(libraryReference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(appCompilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new MessagePack.SourceGenerator.MessagePackGenerator(),
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            appCompilation,
            out Compilation generatedCompilation,
            out ImmutableArray<Diagnostic> driverDiagnostics,
            CancellationToken.None);

        GeneratorDriverRunResult runResult = driver.GetRunResult();
        Assert.All(runResult.Results, result => Assert.Null(result.Exception));
        Assert.DoesNotContain(runResult.Diagnostics.Concat(driverDiagnostics), diagnostic => diagnostic.Id == "CS8785");
        Assert.Contains(
            runResult.Results.SelectMany(result => result.GeneratedSources),
            source => source.HintName == "App.TestResolver.g.cs");

        CompilationWithAnalyzers compilationWithAnalyzers = generatedCompilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new MsgPack00xMessagePackAnalyzer()));
        ImmutableArray<Diagnostic> analyzerDiagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(analyzerDiagnostics, diagnostic => diagnostic.Id == "AD0001");
    }
}
