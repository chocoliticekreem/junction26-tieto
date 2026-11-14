// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class ClosedGenericMetadataTypeReceiptTests
{
    private const string AppSource = """
        using MessagePack;
        using Lib;

        namespace App;

        [MessagePackObject]
        public sealed partial record Cmd([property: Key(0)] string Scope)
        {
            [Key(1)]
            public ITEM_TYPE Member { get; init; }
        }

        [GeneratedMessagePackResolver]
        public partial class TestResolver
        {
        }
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosedGenericValueTypeFromReferencedAssembly_DoesNotCrashGeneratorOrAnalyzer(bool isRecord)
    {
        MetadataReference library = await CreateLibraryReferenceAsync(GenericValueTypeSource(isRecord));
        var test = new VerifyCS.Test(ReferencesSet.MessagePack)
        {
            LanguageVersion = LanguageVersion.Latest,
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
        };
        test.TestState.Sources.Add(AppSource.Replace("ITEM_TYPE", "Item<int>"));
        test.TestState.AdditionalReferences.Add(library);

        await test.RunAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosedGenericValueTypeFromReferencedAssembly_GeneratesResolver(bool isRecord)
    {
        MetadataReference library = await CreateLibraryReferenceAsync(GenericValueTypeSource(isRecord));
        Compilation compilation = await CreateAppCompilationAsync("Item<int>", library);

        AssertResolverGeneratedWithoutGeneratorCrash(compilation);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ClosedGenericClassAndNonGenericStructFromReferencedAssembly_StillGenerateResolver(bool genericClass)
    {
        string librarySource = genericClass
            ? """
                using MessagePack;
                namespace Lib;

                [MessagePackObject]
                public partial class Item<T>
                {
                    [Key(0)]
                    public string Key { get; set; } = "";
                }
                """
            : """
                using MessagePack;
                namespace Lib;

                [MessagePackObject]
                public partial struct Item
                {
                    [Key(0)]
                    public string Key { get; set; }
                }
                """;
        MetadataReference library = await CreateLibraryReferenceAsync(librarySource);
        Compilation compilation = await CreateAppCompilationAsync(genericClass ? "Item<int>" : "Item", library);

        AssertResolverGeneratedWithoutGeneratorCrash(compilation);
    }

    [Fact]
    public async Task ClosedGenericStructDeclaredInSameAssembly_StillGeneratesResolver()
    {
        Compilation compilation = await CreateAppCompilationAsync("Item<int>", additionalSource: GenericValueTypeSource(false));

        AssertResolverGeneratedWithoutGeneratorCrash(compilation);
    }

    private static string GenericValueTypeSource(bool isRecord)
    {
        string declaration = isRecord ? "record struct" : "struct";
        return $$"""
            using MessagePack;
            namespace Lib;

            [MessagePackObject]
            public partial {{declaration}} Item<T>
            {
                [Key(0)]
                public string Key { get; set; }
            }
            """;
    }

    private static async Task<ImmutableArray<MetadataReference>> GetReferencesAsync()
    {
        ImmutableArray<MetadataReference> references = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        return references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
    }

    private static async Task<MetadataReference> CreateLibraryReferenceAsync(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var compilation = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            await GetReferencesAsync(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var image = new MemoryStream();
        var emitResult = compilation.Emit(image);
        emitResult.Success.Should().BeTrue(string.Join(Environment.NewLine, emitResult.Diagnostics));
        return MetadataReference.CreateFromImage(image.ToArray());
    }

    private static async Task<Compilation> CreateAppCompilationAsync(string memberType, MetadataReference? library = null, string? additionalSource = null)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var trees = new List<SyntaxTree>();
        if (additionalSource is not null)
        {
            trees.Add(CSharpSyntaxTree.ParseText(additionalSource, parseOptions));
        }

        trees.Add(CSharpSyntaxTree.ParseText(AppSource.Replace("ITEM_TYPE", memberType), parseOptions));
        ImmutableArray<MetadataReference> references = await GetReferencesAsync();
        if (library is not null)
        {
            references = references.Add(library);
        }

        return CSharpCompilation.Create(
            "App",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static void AssertResolverGeneratedWithoutGeneratorCrash(Compilation compilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out ImmutableArray<Diagnostic> diagnostics);

        diagnostics.Should().NotContain(diagnostic => diagnostic.Id == "CS8785");
        GeneratorDriverRunResult runResult = driver.GetRunResult();
        runResult.Diagnostics.Should().NotContain(diagnostic => diagnostic.Id == "CS8785");
        runResult.Results.SelectMany(result => result.GeneratedSources).Should().Contain(source => source.HintName == "App.TestResolver.g.cs");
    }
}
