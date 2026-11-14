// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ClosedGenericMetadataValueTypeTests
{
    [Theory]
    [InlineData("public partial record struct Item<T>([property: Key(0)] string Key);", "Item<int>")]
    [InlineData("public partial struct Item<T> { [Key(0)] public string Key { get; set; } }", "Item<int>")]
    public async Task ClosedGenericValueTypeFromReferencedAssembly_GeneratesResolverWithoutGeneratorOrAnalyzerCrash(string itemDeclaration, string memberType)
    {
        await RunScenarioAsync(itemDeclaration, memberType, declareItemInApp: false);
    }

    [Theory]
    [InlineData("public partial class Item<T> { [Key(0)] public string Key { get; set; } }", "Item<int>", false)]
    [InlineData("public partial record class Item<T>([property: Key(0)] string Key);", "Item<int>", false)]
    [InlineData("public partial struct Item { [Key(0)] public string Key { get; set; } }", "Item", false)]
    [InlineData("public partial record struct Item([property: Key(0)] string Key);", "Item", false)]
    [InlineData("public partial struct Item<T> { [Key(0)] public string Key { get; set; } }", "Item<int>", true)]
    public async Task PreviouslySupportedGenericAndStructShapes_StillGenerateResolver(string itemDeclaration, string memberType, bool declareItemInApp)
    {
        await RunScenarioAsync(itemDeclaration, memberType, declareItemInApp);
    }

    [Fact]
    public async Task MetadataSymbolNeedingDiagnostic_DoesNotThrow()
    {
        await RunScenarioAsync(
            "public partial struct Item<T> { [Key(0)] public string Key { get; set; } }",
            "Item<int>",
            declareItemInApp: false,
            annotateItem: false,
            expectResolver: false);
    }

    private static async Task RunScenarioAsync(
        string itemDeclaration,
        string memberType,
        bool declareItemInApp,
        bool annotateItem = true,
        bool expectResolver = true)
    {
        List<MetadataReference> references = (await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None)).ToList();
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        string itemAttribute = annotateItem ? "[MessagePackObject]\n" : string.Empty;
        string libItem = declareItemInApp ? string.Empty : itemAttribute + itemDeclaration;
        string appItem = declareItemInApp ? itemAttribute + itemDeclaration : string.Empty;
        string usingLib = declareItemInApp ? string.Empty : "using Lib;";

        string libSource = $$"""
            using MessagePack;
            namespace Lib;
            {{libItem}}
            """;

        CSharpCompilation libCompilation = CSharpCompilation.Create(
            "ReceiptsMetadataLibrary",
            new[] { CSharpSyntaxTree.ParseText(libSource, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var libImage = new MemoryStream();
        var libEmit = libCompilation.Emit(libImage);
        libEmit.Success.Should().BeTrue("the referenced library must compile: {0}", string.Join(Environment.NewLine, libEmit.Diagnostics));

        var appReferences = new List<MetadataReference>(references)
        {
            MetadataReference.CreateFromImage(libImage.ToArray()),
        };

        string appSource = $$"""
            using MessagePack;
            {{usingLib}}
            namespace App;

            {{appItem}}

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
            "ReceiptsApp",
            new[] { CSharpSyntaxTree.ParseText(appSource, new CSharpParseOptions(LanguageVersion.Latest)) },
            appReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(
            appCompilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> generatorDiagnostics);

        CompilationWithAnalyzers compilationWithAnalyzers = appCompilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(
                new MessagePack.SourceGenerator.Analyzers.MsgPack00xMessagePackAnalyzer()));
        ImmutableArray<Diagnostic> analyzerDiagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

        generatorDiagnostics.Where(diagnostic => diagnostic.Id == "CS8785").Should().BeEmpty(
            "the generator must not throw for a closed generic value type obtained from metadata");
        analyzerDiagnostics.Where(diagnostic => diagnostic.Id == "AD0001").Should().BeEmpty(
            "the analyzer must not throw for a closed generic value type obtained from metadata");

        if (expectResolver)
        {
            driver.GetRunResult().Results
                .SelectMany(result => result.GeneratedSources)
                .Select(source => source.HintName)
                .Should().Contain("App.TestResolver.g.cs", "the app must receive its generated resolver");
        }
    }
}
