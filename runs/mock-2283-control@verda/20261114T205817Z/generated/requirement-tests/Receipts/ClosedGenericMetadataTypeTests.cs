// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class ClosedGenericMetadataTypeTests
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
        public partial class TestResolver
        {
        }

        public static class ResolverConsumer
        {
            public static IFormatterResolver Resolver => TestResolver.Instance;
        }
        """;

    [Fact]
    public async Task ClosedGenericRecordStructFromReferencedAssembly_GeneratesResolver()
    {
        MetadataReference library = await CompileLibraryAsync(
            "public partial record struct Item<T>([property: Key(0)] string Key);");

        await RunAppAsync(AppSource, library);
    }

    [Fact]
    public async Task ClosedGenericStructFromReferencedAssembly_GeneratesResolver()
    {
        MetadataReference library = await CompileLibraryAsync("""
            public partial struct Item<T>
            {
                [Key(0)] public string Key { get; set; }
            }
            """);

        await RunAppAsync(AppSource, library);
    }

    [Theory]
    [InlineData("public partial class Item<T> { [Key(0)] public string Key { get; set; } }", "Item<int>")]
    [InlineData("public partial record class Item<T>([property: Key(0)] string Key);", "Item<int>")]
    [InlineData("public partial struct Item { [Key(0)] public string Key { get; set; } }", "Item")]
    [InlineData("public partial record struct Item([property: Key(0)] string Key);", "Item")]
    public async Task OtherMetadataShapes_StillGenerateResolver(string declaration, string memberType)
    {
        MetadataReference library = await CompileLibraryAsync(declaration);
        string appSource = AppSource.Replace("Item<int>", memberType);

        await RunAppAsync(appSource, library);
    }

    private static async Task RunAppAsync(string appSource, MetadataReference library)
    {
        var test = new VerifyCS.Test
        {
            LanguageVersion = LanguageVersion.Latest,
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
        };
        test.TestState.Sources.Add(appSource);
        test.TestState.AdditionalReferences.Add(library);
        await test.RunAsync();
    }

    private static async Task<MetadataReference> CompileLibraryAsync(string declaration)
    {
        string source = $$"""
            using MessagePack;

            namespace Lib;

            [MessagePackObject]
            {{declaration}}
            """;

        ImmutableArray<MetadataReference> references = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        references = references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        var compilation = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var peStream = new MemoryStream();
        EmitResult result = compilation.Emit(peStream);
        result.Success.Should().BeTrue("the referenced library must compile: {0}", string.Join(Environment.NewLine, result.Diagnostics));
        return MetadataReference.CreateFromImage(peStream.ToArray());
    }
}
