// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;
using MessagePack.SourceGenerator;
using MessagePack.SourceGenerator.Analyzers;

namespace Receipts.GeneratedTests;

public class ClosedGenericMetadataStructReceiptTests
{
    [Fact]
    public async Task MetadataRecordStructFromReferencedAssemblyDoesNotCrashGeneratorOrAnalyzer()
    {
        const string libSource = """
            using MessagePack;

            namespace Lib;

            [MessagePackObject]
            public partial record struct Item<T>([property: Key(0)] string Key);
            """;

        const string appSource = """
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
            public sealed partial class TestResolver
            {
            }
            """;

        var frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        MetadataReference[] references = frameworkReferences
            .Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack))
            .ToArray();

        CSharpCompilation libCompilation = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(libSource, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        using MemoryStream peStream = new();
        EmitResult emitResult = libCompilation.Emit(peStream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference libReference = MetadataReference.CreateFromImage(peStream.ToArray());

        CSharpCompilation appCompilation = CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(appSource, new CSharpParseOptions(LanguageVersion.Latest)) },
            references.Concat(new[] { libReference }),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePackGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(
            appCompilation,
            out Compilation _,
            out ImmutableArray<Diagnostic> _);

        GeneratorDriverRunResult runResult = driver.GetRunResult();
        Assert.All(runResult.Results, static result => Assert.Null(result.Exception));
        Assert.Contains(
            runResult.Results.SelectMany(static result => result.GeneratedSources),
            static source => source.HintName == "App.TestResolver.g.cs");

        ImmutableArray<Diagnostic> analyzerDiagnostics = await appCompilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new MsgPack00xMessagePackAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(analyzerDiagnostics, static diagnostic => diagnostic.Id == "AD0001");
    }
}
