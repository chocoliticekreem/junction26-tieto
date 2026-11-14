// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using MessagePack.SourceGenerator;
using MessagePack.SourceGenerator.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ClosedGenericMetadataValueTypeTests
{
    private const string AppSource = """
        using MessagePack;
        using Lib;

        namespace App;

        [MessagePackObject]
        public sealed partial record Cmd([property: Key(0)] string Scope)
        {
            [Key(1)]
            public Item<int> Member { get; init; }
        }

        [GeneratedMessagePackResolver]
        public partial class AppResolver
        {
        }
        """;

    [Fact]
    public async Task ClosedGenericStructFromMetadata_GeneratesResolverWithoutCrash()
    {
        const string itemDeclaration = """
            [MessagePackObject]
            public partial struct Item<T>
            {
                [Key(0)]
                public string Key { get; set; }
            }
            """;

        await AssertNoGeneratorOrAnalyzerCrashAsync(itemDeclaration, expectGeneratedResolver: true);
    }

    [Fact]
    public async Task ClosedGenericRecordStructFromMetadata_GeneratesResolverWithoutCrash()
    {
        const string itemDeclaration = """
            [MessagePackObject]
            public partial record struct Item<T>([property: Key(0)] string Key);
            """;

        await AssertNoGeneratorOrAnalyzerCrashAsync(itemDeclaration, expectGeneratedResolver: true);
    }

    [Fact]
    public async Task MetadataMemberDiagnosticsWithoutSourceLocation_DoNotCrashGeneratorOrAnalyzer()
    {
        const string itemDeclaration = """
            [MessagePackObject]
            public partial struct Item<T>
            {
                [Key(null)]
                public string NullKey { get; set; }

                public string MissingKey { get; set; }
            }
            """;

        await AssertNoGeneratorOrAnalyzerCrashAsync(itemDeclaration, expectGeneratedResolver: false);
    }

    private static async Task AssertNoGeneratorOrAnalyzerCrashAsync(string itemDeclaration, bool expectGeneratedResolver)
    {
        string librarySource = $$"""
            using MessagePack;

            namespace Lib;

            {{itemDeclaration}}
            """;

        ImmutableArray<MetadataReference> references = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        references = references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);

        CSharpCompilation libraryCompilation = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(librarySource, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var libraryImage = new MemoryStream();
        var emitResult = libraryCompilation.Emit(libraryImage);
        emitResult.Success.Should().BeTrue(string.Join(Environment.NewLine, emitResult.Diagnostics));

        ImmutableArray<MetadataReference> appReferences = references.Add(MetadataReference.CreateFromImage(libraryImage.ToArray()));
        CSharpCompilation appCompilation = CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource, parseOptions) },
            appReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePackGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(appCompilation, out Compilation outputCompilation, out ImmutableArray<Diagnostic> generatorDiagnostics);
        GeneratorDriverRunResult runResult = driver.GetRunResult();

        generatorDiagnostics.Should().NotContain(diagnostic => diagnostic.Id == "CS8785");
        runResult.Results.Should().OnlyContain(result => result.Exception is null);

        if (expectGeneratedResolver)
        {
            runResult.GeneratedTrees.Should().Contain(tree => tree.FilePath.EndsWith("AppResolver.g.cs", StringComparison.Ordinal));
        }

        var analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new MsgPack00xMessagePackAnalyzer());
        ImmutableArray<Diagnostic> analyzerDiagnostics = await outputCompilation.WithAnalyzers(analyzers).GetAllDiagnosticsAsync();
        analyzerDiagnostics.Should().NotContain(diagnostic => diagnostic.Id == "AD0001");
    }
}
