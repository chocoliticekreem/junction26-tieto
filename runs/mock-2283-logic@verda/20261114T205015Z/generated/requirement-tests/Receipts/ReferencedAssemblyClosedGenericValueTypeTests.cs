// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using AnalyzerUnderTest = MessagePack.SourceGenerator.Analyzers.MsgPack00xMessagePackAnalyzer;
using GeneratorUnderTest = MessagePack.SourceGenerator.MessagePackGenerator;

namespace Receipts.GeneratedTests;

public class ReferencedAssemblyClosedGenericValueTypeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosedGenericValueTypeFromReferencedAssembly_GeneratesResolverAndDoesNotThrow(bool isRecordStruct)
    {
        string itemDeclaration = isRecordStruct
            ? """
              [MessagePackObject]
              public partial record struct Item<T>([property: Key(0)] string Key);
              """
            : """
              [MessagePackObject]
              public partial struct Item<T>
              {
                  [Key(0)]
                  public string Key { get; set; }
              }
              """;

        await RunScenarioAsync(itemDeclaration, "Item<int>");
    }

    [Theory]
    [InlineData("missing-key")]
    [InlineData("null-key")]
    public async Task MetadataDiagnosticPaths_DoNotCrashOrLeakIntoConsumer(string memberDefect)
    {
        string keyAttribute = memberDefect == "null-key" ? "[Key(null)]" : string.Empty;
        string itemDeclaration = $$"""
            [MessagePackObject]
            public partial struct Item<T>
            {
                {{keyAttribute}}
                public string Key { get; set; }
            }
            """;

        await RunScenarioAsync(itemDeclaration, "Item<int>");
    }

    [Theory]
    [InlineData("generic-class")]
    [InlineData("non-generic-struct")]
    [InlineData("same-assembly-record-struct")]
    public async Task PreviouslyWorkingShapes_StillGenerateResolver(string shape)
    {
        string libraryDeclaration;
        string memberType;
        string applicationDeclaration = string.Empty;

        switch (shape)
        {
            case "generic-class":
                libraryDeclaration = """
                    [MessagePackObject]
                    public partial class Item<T>
                    {
                        [Key(0)]
                        public string Key { get; set; }
                    }
                    """;
                memberType = "Item<int>";
                break;
            case "non-generic-struct":
                libraryDeclaration = """
                    [MessagePackObject]
                    public partial struct Item
                    {
                        [Key(0)]
                        public string Key { get; set; }
                    }
                    """;
                memberType = "Item";
                break;
            case "same-assembly-record-struct":
                libraryDeclaration = "public sealed class Placeholder { }";
                memberType = "Item<int>";
                applicationDeclaration = """
                    [MessagePackObject]
                    public partial record struct Item<T>([property: Key(0)] string Key);
                    """;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }

        await RunScenarioAsync(libraryDeclaration, memberType, applicationDeclaration);
    }

    private static async Task RunScenarioAsync(string libraryItemDeclaration, string memberType, string applicationItemDeclaration = "")
    {
        ImmutableArray<MetadataReference> references = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        references = references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        string librarySource = $$"""
            using MessagePack;
            namespace Lib;

            {{libraryItemDeclaration}}
            """;
        var libraryCompilation = CSharpCompilation.Create(
            "ReceiptsReferencedLibrary",
            new[] { CSharpSyntaxTree.ParseText(librarySource, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var libraryImage = new MemoryStream();
        var emitResult = libraryCompilation.Emit(libraryImage);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference libraryReference = MetadataReference.CreateFromImage(libraryImage.ToArray());

        string applicationSource = $$"""
            using MessagePack;
            using Lib;
            namespace App;

            {{applicationItemDeclaration}}

            [MessagePackObject]
            public sealed partial record Cmd([property: Key(0)] string Scope)
            {
                [Key(1)] public {{memberType}} Member { get; init; }
            }

            [GeneratedMessagePackResolver]
            public partial class TestResolver
            {
            }
            """;
        var applicationCompilation = CSharpCompilation.Create(
            "ReceiptsApplication",
            new[] { CSharpSyntaxTree.ParseText(applicationSource, parseOptions) },
            references.Add(libraryReference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new GeneratorUnderTest(), parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(applicationCompilation, out Compilation outputCompilation, out ImmutableArray<Diagnostic> generatorDiagnostics);
        GeneratorRunResult generatorResult = Assert.Single(driver.GetRunResult().Results);

        Assert.Null(generatorResult.Exception);
        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Id == "CS8785");
        Assert.Contains(generatorResult.GeneratedSources, source => source.HintName == "App.TestResolver.g.cs");
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        CompilationWithAnalyzers applicationWithAnalyzer = applicationCompilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new AnalyzerUnderTest()));
        ImmutableArray<Diagnostic> analyzerDiagnostics = await applicationWithAnalyzer.GetAnalyzerDiagnosticsAsync();

        Assert.DoesNotContain(analyzerDiagnostics, diagnostic => diagnostic.Id == "AD0001");
        Assert.DoesNotContain(analyzerDiagnostics, diagnostic => diagnostic.Id.StartsWith("MsgPack", StringComparison.Ordinal));
    }
}
