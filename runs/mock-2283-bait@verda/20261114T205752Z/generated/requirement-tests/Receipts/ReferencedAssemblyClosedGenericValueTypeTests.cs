// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack.SourceGenerator;
using MessagePack.SourceGenerator.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ReferencedAssemblyClosedGenericValueTypeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosedGenericValueTypeFromReferencedAssembly_GeneratesResolverWithoutGeneratorOrAnalyzerCrash(bool isRecord)
    {
        string itemDeclaration = isRecord
            ? "public partial record struct Item<T>([property: Key(0)] string Key);"
            : "public partial struct Item<T> { [Key(0)] public string Key { get; set; } }";
        CSharpCompilation appCompilation = await CreateAppCompilationAsync(itemDeclaration);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePackGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(
            appCompilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> generatorDiagnostics);
        GeneratorDriverRunResult runResult = driver.GetRunResult();

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Id == "CS8785");
        Assert.DoesNotContain(runResult.Results, result => result.Exception is not null);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        string generatedSource = string.Join("\n", runResult.GeneratedTrees.Select(tree => tree.ToString()));
        Assert.Contains("GeneratedMessagePackResolver", generatedSource);

        ImmutableArray<Diagnostic> analyzerDiagnostics = await appCompilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new MsgPack00xMessagePackAnalyzer()))
            .GetAllDiagnosticsAsync();
        Assert.DoesNotContain(analyzerDiagnostics, diagnostic => diagnostic.Id == "AD0001");
    }

    private static async Task<CSharpCompilation> CreateAppCompilationAsync(string itemDeclaration)
    {
        var references = new List<MetadataReference>();
        references.AddRange(await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None));
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);

        string librarySource = $$"""
            using MessagePack;
            namespace Lib;

            [MessagePackObject]
            {{itemDeclaration}}
            """;
        CSharpCompilation libraryCompilation = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(librarySource, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var libraryImage = new MemoryStream();
        EmitResult emitResult = libraryCompilation.Emit(libraryImage);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        IEnumerable<MetadataReference> appReferences = references.Append(MetadataReference.CreateFromImage(libraryImage.ToArray()));
        const string AppSource = """
            using MessagePack;
            using Lib;
            namespace App;

            [MessagePackObject]
            public sealed partial record Cmd([property: Key(0)] string Scope)
            {
                [Key(1)]
                public Item<int> Member { get; init; }
            }
            """;
        return CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource, parseOptions) },
            appReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
