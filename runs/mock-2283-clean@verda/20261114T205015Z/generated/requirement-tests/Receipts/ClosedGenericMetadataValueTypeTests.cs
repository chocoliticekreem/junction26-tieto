// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack.SourceGenerator;
using MessagePack.SourceGenerator.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ClosedGenericMetadataValueTypeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosedGenericValueTypeFromReferencedAssembly_DoesNotCrashAndGeneratesResolver(bool recordStruct)
    {
        string libSource = recordStruct
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
                    public Item(string key) => Key = key;

                    [Key(0)]
                    public string Key { get; set; }
                }
                """;

        ImmutableArray<MetadataReference> frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        ImmutableArray<MetadataReference> references = frameworkReferences.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation libCompilation = CreateCompilation("Lib", libSource, references);
        GeneratorDriver libDriver = CreateDriver(libCompilation);
        libDriver = libDriver.RunGeneratorsAndUpdateCompilation(libCompilation, out Compilation libOutput, out ImmutableArray<Diagnostic> libGeneratorDiagnostics);
        Assert.DoesNotContain(libGeneratorDiagnostics, diagnostic => diagnostic.Id == "CS8785");

        using MemoryStream libImage = new();
        var libEmit = libOutput.Emit(libImage);
        Assert.True(libEmit.Success, string.Join(Environment.NewLine, libEmit.Diagnostics));
        MetadataReference libReference = MetadataReference.CreateFromImage(libImage.ToArray());

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
            public partial class GeneratedMessagePackResolver
            {
            }
            """;

        CSharpCompilation appCompilation = CreateCompilation("App", appSource, references.Add(libReference));
        GeneratorDriver appDriver = CreateDriver(appCompilation);
        appDriver = appDriver.RunGeneratorsAndUpdateCompilation(appCompilation, out Compilation appOutput, out ImmutableArray<Diagnostic> appGeneratorDiagnostics);
        GeneratorDriverRunResult runResult = appDriver.GetRunResult();

        CompilationWithAnalyzers appWithAnalyzers = appOutput.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new MsgPack00xMessagePackAnalyzer()));
        ImmutableArray<Diagnostic> analyzerDiagnostics = await appWithAnalyzers.GetAnalyzerDiagnosticsAsync();

        Assert.DoesNotContain(appGeneratorDiagnostics, diagnostic => diagnostic.Id == "CS8785");
        Assert.DoesNotContain(analyzerDiagnostics, diagnostic => diagnostic.Id == "AD0001");
        Assert.Contains(
            runResult.Results.SelectMany(result => result.GeneratedSources),
            source => source.HintName == "App.GeneratedMessagePackResolver.g.cs");

        using MemoryStream appImage = new();
        var appEmit = appOutput.Emit(appImage);
        Assert.True(appEmit.Success, string.Join(Environment.NewLine, appEmit.Diagnostics));
    }

    private static CSharpCompilation CreateCompilation(string assemblyName, string source, ImmutableArray<MetadataReference> references)
    {
        CSharpSyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        return CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable));
    }

    private static GeneratorDriver CreateDriver(CSharpCompilation compilation)
    {
        CSharpParseOptions parseOptions = (CSharpParseOptions)compilation.SyntaxTrees.First().Options;
        return CSharpGeneratorDriver.Create(
            generators: new[] { new MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
    }
}
