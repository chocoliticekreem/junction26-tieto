// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class Req2283ClosedGenericMetadataStructTests
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosedGenericValueTypeFromReferencedAssemblyDoesNotCrash(bool useRecordStruct)
    {
        string librarySource = useRecordStruct
            ? """
                using MessagePack;

                namespace Lib;

                [MessagePackObject]
                public partial record struct Item<T>([property: Key(0)] string Key);
                """
            : """
                using MessagePack;

                namespace Lib;

                [MessagePackObject]
                public partial struct Item<T>
                {
                    [Key(0)]
                    public T Key { get; set; }
                }
                """;

        await AssertCleanGenerationAsync(librarySource, AppSource);
    }

    [Fact]
    public async Task MetadataDiagnosticWithoutSourceLocationDoesNotCrashConsumer()
    {
        string librarySource = """
            using MessagePack;

            namespace Lib;

            [MessagePackObject]
            public partial struct Item<T>
            {
                public T Value { get; set; }
            }
            """;

        await AssertCleanGenerationAsync(librarySource, AppSource);
    }

    [Theory]
    [InlineData("class")]
    [InlineData("struct")]
    public async Task PreviouslyWorkingMetadataShapesStillGenerateResolver(string shape)
    {
        string librarySource;
        string appSource;
        if (shape == "class")
        {
            librarySource = """
                using MessagePack;

                namespace Lib;

                [MessagePackObject]
                public partial class Item<T>
                {
                    [Key(0)]
                    public T Key { get; set; }
                }
                """;
            appSource = AppSource;
        }
        else
        {
            librarySource = """
                using MessagePack;

                namespace Lib;

                [MessagePackObject]
                public partial struct Item
                {
                    [Key(0)]
                    public int Key { get; set; }
                }
                """;
            appSource = AppSource.Replace("Item<int>", "Item");
        }

        await AssertCleanGenerationAsync(librarySource, appSource);
    }

    [Fact]
    public async Task ClosedGenericValueTypeInSameAssemblyStillGeneratesResolver()
    {
        string appSource = """
            using MessagePack;

            namespace App;

            [MessagePackObject]
            public partial record struct Item<T>([property: Key(0)] T Key);

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

        await AssertCleanGenerationAsync(null, appSource);
    }

    private static async Task AssertCleanGenerationAsync(string? librarySource, string appSource)
    {
        MetadataReference[] additionalReferences;
        if (librarySource is null)
        {
            additionalReferences = [];
        }
        else
        {
            additionalReferences = [await CreateLibraryReferenceAsync(librarySource)];
        }

        Compilation appCompilation = await CreateCompilationAsync(appSource, "App", additionalReferences);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(
            appCompilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> generatorDiagnostics);

        Assert.DoesNotContain(
            generatorDiagnostics,
            diagnostic => diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error);

        CompilationWithAnalyzers compilationWithAnalyzers = outputCompilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(
                new MessagePack.SourceGenerator.Analyzers.MsgPack00xMessagePackAnalyzer()));
        ImmutableArray<Diagnostic> analyzerDiagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(
            analyzerDiagnostics,
            diagnostic => diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error);

        GeneratorDriverRunResult runResult = driver.GetRunResult();
        Assert.Contains(
            runResult.Results.SelectMany(result => result.GeneratedSources),
            source => source.HintName == "App.AppResolver.g.cs");
    }

    private static async Task<MetadataReference> CreateLibraryReferenceAsync(string source)
    {
        Compilation libraryCompilation = await CreateCompilationAsync(source, "Lib");
        using var peStream = new MemoryStream();
        EmitResult emitResult = libraryCompilation.Emit(peStream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        return MetadataReference.CreateFromImage(peStream.ToArray());
    }

    private static async Task<CSharpCompilation> CreateCompilationAsync(
        string source,
        string assemblyName,
        params MetadataReference[] additionalReferences)
    {
        var references = new List<MetadataReference>();
        references.AddRange(await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None));
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        references.AddRange(additionalReferences);

        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Latest));
        return CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
