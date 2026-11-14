// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ClosedGenericStructFromReferenceTests(ITestOutputHelper logger)
{
    private const string LibSource = """
        using MessagePack;
        namespace Lib
        {
            [MessagePackObject]
            public struct Item<T>
            {
                [Key(0)]
                public string Key { get; set; }
            }
        }
        """;

    private const string AppSource = """
        using MessagePack;
        using Lib;
        namespace App
        {
            [MessagePackObject]
            public class Cmd
            {
                [Key(0)]
                public string Scope { get; set; }

                [Key(1)]
                public Item<int> Member { get; set; }
            }
        }
        """;

    [Fact]
    public async Task GeneratorDoesNotThrowOnClosedGenericStructFromReferencedAssembly()
    {
        CSharpParseOptions parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        ImmutableArray<MetadataReference> references = (await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None))
            .AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(LibSource, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        var emitResult = lib.Emit(stream);
        foreach (var d in emitResult.Diagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        Assert.True(emitResult.Success);
        MetadataReference libReference = MetadataReference.CreateFromImage(stream.ToArray());

        CSharpCompilation app = CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource, parseOptions) },
            references.Add(libReference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(app, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        foreach (var d in generatorDiagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        GeneratorDriverRunResult runResult = driver.GetRunResult();
        foreach (var r in runResult.Results)
        {
            Assert.Null(r.Exception);
        }

        Assert.DoesNotContain(generatorDiagnostics, d => d.Id == "CS8785");
        Assert.NotEmpty(runResult.GeneratedTrees);

        ImmutableArray<DiagnosticAnalyzer> analyzers = ImmutableArray.Create<DiagnosticAnalyzer>(new MessagePack.SourceGenerator.Analyzers.MsgPack00xMessagePackAnalyzer());
        ImmutableArray<Diagnostic> analyzerDiagnostics = await app.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();
        foreach (var d in analyzerDiagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        Assert.DoesNotContain(analyzerDiagnostics, d => d.Id == "AD0001");
    }
}
