// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class Req2283ReferencedGenericStructResolverTests
{
    private const string AppSource = """
        using MessagePack;
        using Lib;

        namespace App
        {
            [GeneratedMessagePackResolver]
            partial class AppResolver { }

            [MessagePackObject]
            public sealed partial record Cmd([property: Key(0)] string Scope)
            {
                [Key(1)] public Item<int> Member { get; init; }
            }
        }
        """;

    private const string LibRecordStructSource = """
        using MessagePack;

        namespace Lib
        {
            [MessagePackObject]
            public partial record struct Item<T>([property: Key(0)] string Key);
        }
        """;

    private const string LibStructSource = """
        using MessagePack;

        namespace Lib
        {
            [MessagePackObject]
            public struct Item<T>
            {
                [Key(0)] public string Key { get; set; }
            }
        }
        """;

    [Fact]
    public async Task ResolverIsGenerated_WhenMemberIsReferencedGenericRecordStruct()
    {
        await AssertResolverGeneratedAsync(LibRecordStructSource);
    }

    [Fact]
    public async Task ResolverIsGenerated_WhenMemberIsReferencedGenericStruct()
    {
        await AssertResolverGeneratedAsync(LibStructSource);
    }

    private static async Task AssertResolverGeneratedAsync(string libSource)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp12);
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> baseReferences = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();

        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(libSource, parseOptions) },
            baseReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitResult = lib.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference libReference = MetadataReference.CreateFromImage(stream.ToArray());

        CSharpCompilation app = CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource, parseOptions) },
            baseReferences.Append(libReference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGenerators(app);
        GeneratorDriverRunResult runResult = driver.GetRunResult();

        foreach (GeneratorRunResult result in runResult.Results)
        {
            Assert.True(result.Exception is null, $"Generator threw: {result.Exception}");
        }

        Assert.DoesNotContain(runResult.Diagnostics, d => d.Id == "CS8785");
        Assert.Contains(
            runResult.Results.SelectMany(r => r.GeneratedSources),
            s => s.HintName.Contains("AppResolver"));
    }
}
