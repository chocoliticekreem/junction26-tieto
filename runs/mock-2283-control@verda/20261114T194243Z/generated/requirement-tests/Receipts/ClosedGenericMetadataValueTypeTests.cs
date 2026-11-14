// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ClosedGenericMetadataValueTypeTests
{
    [Fact]
    public async Task ClosedGenericRecordStructFromMetadata_GeneratesResolverWithoutExceptions()
    {
        await VerifyAsync(
            """
            [MessagePackObject]
            public partial record struct Item<T>([property: Key(0)] string Key);
            """,
            "Item<int>");
    }

    [Fact]
    public async Task ClosedGenericStructFromMetadata_GeneratesResolverWithoutExceptions()
    {
        await VerifyAsync(
            """
            [MessagePackObject]
            public partial struct Item<T>
            {
                [Key(0)]
                public string Key { get; set; }
            }
            """,
            "Item<int>");
    }

    [Fact]
    public async Task NonGenericStructFromMetadata_StillGeneratesResolver()
    {
        await VerifyAsync(
            """
            [MessagePackObject]
            public partial struct Item
            {
                [Key(0)]
                public string Key { get; set; }
            }
            """,
            "Item");
    }

    [Fact]
    public async Task ClosedGenericClassFromMetadata_StillGeneratesResolver()
    {
        await VerifyAsync(
            """
            [MessagePackObject]
            public partial class Item<T>
            {
                [Key(0)]
                public string Key { get; set; } = string.Empty;
            }
            """,
            "Item<int>");
    }

    private static async Task VerifyAsync(string itemDeclaration, string memberType)
    {
        string librarySource = "using MessagePack;\nnamespace Lib;\n" + itemDeclaration;
        string appSource = $$"""
            using MessagePack;
            using Lib;

            namespace App;

            [GeneratedMessagePackResolver]
            public partial class AppResolver
            {
            }

            [MessagePackObject]
            public sealed partial record Cmd([property: Key(0)] string Scope)
            {
                [Key(1)]
                public {{memberType}} Member { get; init; }
            }
            """;

        ImmutableArray<MetadataReference> frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        ImmutableArray<MetadataReference> commonReferences = frameworkReferences.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        CSharpParseOptions parseOptions = new(LanguageVersion.Latest);
        CSharpCompilationOptions compilationOptions = new(OutputKind.DynamicallyLinkedLibrary);

        CSharpCompilation libraryCompilation = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(librarySource, parseOptions) },
            commonReferences,
            compilationOptions);

        using MemoryStream libraryImage = new();
        EmitResult emitResult = libraryCompilation.Emit(libraryImage);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference libraryReference = MetadataReference.CreateFromImage(libraryImage.ToArray());

        CSharpCompilation appCompilation = CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(appSource, parseOptions) },
            commonReferences.Add(libraryReference),
            compilationOptions);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(appCompilation, out _, out ImmutableArray<Diagnostic> generatorDiagnostics);
        Assert.DoesNotContain(generatorDiagnostics, static diagnostic => diagnostic.Id == "CS8785");

        ImmutableArray<DiagnosticAnalyzer> analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new MessagePack.SourceGenerator.Analyzers.MsgPack00xMessagePackAnalyzer());
        ImmutableArray<Diagnostic> analyzerDiagnostics = await appCompilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(analyzerDiagnostics, static diagnostic => diagnostic.Id == "AD0001");

        GeneratorDriverRunResult runResult = driver.GetRunResult();
        Assert.Contains(
            runResult.Results.SelectMany(static result => result.GeneratedSources),
            static source => source.HintName == "App.AppResolver.g.cs");
    }
}
