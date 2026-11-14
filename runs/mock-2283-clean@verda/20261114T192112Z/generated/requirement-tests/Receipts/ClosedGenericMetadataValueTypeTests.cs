// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
            [Key(1)] public Item<int> Member { get; init; }
        }

        [GeneratedMessagePackResolver]
        public partial class AppResolver
        {
        }
        """;

    [Fact]
    public Task ClosedGenericRecordStructFromReferencedAssembly_GeneratesResolverWithoutCrashes() =>
        AssertGeneratesResolverWithoutCrashes("""
            using MessagePack;

            namespace Lib;

            [MessagePackObject]
            public partial record struct Item<T>([property: Key(0)] string Key);
            """);

    [Fact]
    public Task ClosedGenericStructFromReferencedAssembly_GeneratesResolverWithoutCrashes() =>
        AssertGeneratesResolverWithoutCrashes("""
            using MessagePack;

            namespace Lib;

            [MessagePackObject]
            public partial struct Item<T>
            {
                [Key(0)] public string Key { get; set; }
            }
            """);

    [Fact]
    public Task MetadataGenericValueTypeWithoutMemberKeys_DoesNotCrashLocationDiagnostics() =>
        AssertGeneratesResolverWithoutCrashes("""
            using MessagePack;

            namespace Lib;

            [MessagePackObject]
            public partial struct Item<T>
            {
                public T Value { get; set; }
            }
            """);

    private static async Task AssertGeneratesResolverWithoutCrashes(string librarySource)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var references = new List<MetadataReference>();
        references.AddRange(await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None));
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        var libraryCompilation = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(librarySource, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var libraryImage = new MemoryStream();
        var libraryEmit = libraryCompilation.Emit(libraryImage);
        Assert.True(libraryEmit.Success, string.Join(Environment.NewLine, libraryEmit.Diagnostics));

        references.Add(MetadataReference.CreateFromImage(libraryImage.ToArray()));
        var appCompilation = CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(appCompilation, out var updatedCompilation, out var generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Id == "CS8785");
        Assert.Contains(
            updatedCompilation.SyntaxTrees,
            tree => tree.FilePath.EndsWith("App.AppResolver.g.cs", StringComparison.Ordinal));

        var analyzerDiagnostics = await updatedCompilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new MessagePack.SourceGenerator.Analyzers.MsgPack00xMessagePackAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(analyzerDiagnostics, diagnostic => diagnostic.Id == "AD0001");
    }
}
