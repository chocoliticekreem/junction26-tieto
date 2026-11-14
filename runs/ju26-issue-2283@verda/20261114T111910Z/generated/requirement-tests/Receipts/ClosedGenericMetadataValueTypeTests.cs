// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ClosedGenericValueTypeFromMetadataDoesNotCrash(bool recordStruct)
    {
        MetadataReference library = await CompileLibraryAsync(GetGenericValueTypeDeclaration(recordStruct));

        await RunAppVerifierAsync(library);
    }

    [Fact]
    public async Task ClosedGenericRecordStructFromMetadataGeneratesResolver()
    {
        MetadataReference library = await CompileLibraryAsync(GetGenericValueTypeDeclaration(recordStruct: true));
        ImmutableArray<MetadataReference> references = (await GetReferencesAsync()).Add(library);
        CSharpParseOptions parseOptions = new(LanguageVersion.Latest);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsApp",
            new[] { CSharpSyntaxTree.ParseText(AppSource, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        ISourceGenerator generator = new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { generator }, parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out ImmutableArray<Diagnostic> diagnostics);

        diagnostics.Should().NotContain(d => d.Id == "CS8785");
        GeneratorDriverRunResult result = driver.GetRunResult();
        var generatedSources = result.Results.SelectMany(static r => r.GeneratedSources);
        generatedSources.Should().Contain(s => s.HintName.EndsWith("AppResolver.g.cs", StringComparison.Ordinal));
        outputCompilation.GetDiagnostics().Should().NotContain(d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task MetadataMemberDiagnosticWithoutSyntaxDoesNotCrash()
    {
        const string declaration = "public partial struct Item<T> { public string Key { get; set; } = string.Empty; }";
        MetadataReference library = await CompileLibraryAsync(declaration);

        // The missing-key diagnostic belongs to Lib. Consuming Item<int> must neither
        // crash the analyzer/generator nor report a diagnostic at a nonexistent App location.
        await RunAppVerifierAsync(library);
    }

    [Theory]
    [InlineData("public partial class Item<T> { [Key(0)] public string Key { get; set; } = string.Empty; }", "Item<int>", false)]
    [InlineData("public partial struct Item { [Key(0)] public string Key { get; set; } = string.Empty; }", "Item", false)]
    [InlineData("public partial record struct Item { [Key(0)] public string Key { get; set; } = string.Empty; }", "Item", false)]
    [InlineData("public partial record struct Item<T>([property: Key(0)] string Key);", "Item<int>", true)]
    public async Task PreviouslySupportedAndSameAssemblyTypesStillWork(string declaration, string memberType, bool declareInApp)
    {
        MetadataReference? library = declareInApp ? null : await CompileLibraryAsync(declaration);

        await RunAppVerifierAsync(library, declareInApp ? declaration : null, memberType);
    }

    private static string GetGenericValueTypeDeclaration(bool recordStruct) => recordStruct
        ? "public partial record struct Item<T>([property: Key(0)] string Key);"
        : "public partial struct Item<T> { [Key(0)] public string Key { get; set; } = string.Empty; }";

    private static string GetLibrarySource(string declaration) => $$"""
        using MessagePack;

        namespace Lib;

        [MessagePackObject]
        {{declaration}}
        """;

    private static async Task RunAppVerifierAsync(MetadataReference? library, string? sameAssemblyDeclaration = null, string memberType = "Item<int>")
    {
        VerifyCS.Test test = new()
        {
            LanguageVersion = LanguageVersion.Latest,
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
        };

        if (sameAssemblyDeclaration is not null)
        {
            test.TestState.Sources.Add(GetLibrarySource(sameAssemblyDeclaration));
        }

        string appSource = AppSource.Replace("Item<int> Member", memberType + " Member", StringComparison.Ordinal);
        test.TestState.Sources.Add(appSource);
        if (library is not null)
        {
            test.TestState.AdditionalReferences.Add(library);
        }

        await test.RunAsync();
    }

    private static async Task<MetadataReference> CompileLibraryAsync(string declaration)
    {
        CSharpParseOptions parseOptions = new(LanguageVersion.Latest);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsLib",
            new[] { CSharpSyntaxTree.ParseText(GetLibrarySource(declaration), parseOptions) },
            await GetReferencesAsync(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        using var peStream = new MemoryStream();
        var emitResult = compilation.Emit(peStream);
        emitResult.Success.Should().BeTrue($"the library failed to compile: {string.Join(Environment.NewLine, emitResult.Diagnostics)}");
        return MetadataReference.CreateFromImage(peStream.ToArray());
    }

    private static async Task<ImmutableArray<MetadataReference>> GetReferencesAsync()
    {
        ImmutableArray<MetadataReference> references = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        return references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
    }
}
