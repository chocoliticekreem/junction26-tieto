// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class Req2283ClosedGenericMetadataTests
{
    [Theory]
    [InlineData("public partial struct Item<T> { [Key(0)] public string Key { get; set; } }")]
    [InlineData("public partial record struct Item<T>([property: Key(0)] string Key);")]
    public async Task ClosedGenericValueTypeFromMetadata_DoesNotCrashGeneratorOrAnalyzer(string itemDeclaration)
    {
        await VerifyAppCompilesCleanlyAsync(itemDeclaration, "Item<int>");
    }

    [Fact]
    public async Task ClosedGenericRecordStructFromMetadata_GeneratesResolver()
    {
        MetadataReference library = await CompileLibraryAsync(
            "public partial record struct Item<T>([property: Key(0)] string Key);");

        ImmutableArray<MetadataReference> references = (await GetFrameworkAndMessagePackReferencesAsync()).Add(library);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "App_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(CreateAppSource("Item<int>"), new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        ISourceGenerator generator = new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _, CancellationToken.None);

        GeneratorRunResult result = Assert.Single(driver.GetRunResult().Results);
        Assert.Null(result.Exception);
        Assert.Contains(result.GeneratedSources, source => source.HintName == "App.AppResolver.g.cs");
    }

    [Theory]
    [InlineData("public partial struct Item { [Key(0)] public string Key { get; set; } }", "Item")]
    [InlineData("public partial record struct Item([property: Key(0)] string Key);", "Item")]
    [InlineData("public partial class Item<T> { [Key(0)] public string Key { get; set; } }", "Item<int>")]
    [InlineData("public partial record class Item<T>([property: Key(0)] string Key);", "Item<int>")]
    public async Task PreviouslyWorkingMetadataTypes_StillCompileCleanly(string itemDeclaration, string memberType)
    {
        await VerifyAppCompilesCleanlyAsync(itemDeclaration, memberType);
    }

    private static async Task VerifyAppCompilesCleanlyAsync(string itemDeclaration, string memberType)
    {
        MetadataReference library = await CompileLibraryAsync(itemDeclaration);
        var test = new VerifyCS.Test
        {
            LanguageVersion = LanguageVersion.Latest,
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
        };
        test.TestState.Sources.Add(CreateAppSource(memberType));
        test.TestState.AdditionalReferences.Add(library);
        await test.RunAsync();
    }

    private static async Task<MetadataReference> CompileLibraryAsync(string itemDeclaration)
    {
        string source = $$"""
            using MessagePack;
            namespace Lib;

            [MessagePackObject]
            {{itemDeclaration}}
            """;
        ImmutableArray<MetadataReference> references = await GetFrameworkAndMessagePackReferencesAsync();
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Lib_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var peStream = new MemoryStream();
        EmitResult emitResult = compilation.Emit(peStream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        return MetadataReference.CreateFromImage(peStream.ToArray());
    }

    private static async Task<ImmutableArray<MetadataReference>> GetFrameworkAndMessagePackReferencesAsync()
    {
        ImmutableArray<MetadataReference> references = await ReferenceAssemblies.Net.Net80.ResolveAsync(
            LanguageNames.CSharp,
            CancellationToken.None);
        return references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
    }

    private static string CreateAppSource(string memberType) => $$"""
        using MessagePack;
        using Lib;
        namespace App;

        [MessagePackObject]
        public sealed partial record Cmd([property: Key(0)] string Scope)
        {
            [Key(1)]
            public {{memberType}} Member { get; init; }
        }

        [GeneratedMessagePackResolver]
        public partial class AppResolver
        {
        }
        """;
}
