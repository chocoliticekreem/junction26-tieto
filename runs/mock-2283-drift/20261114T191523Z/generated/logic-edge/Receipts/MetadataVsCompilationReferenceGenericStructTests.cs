// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class MetadataVsCompilationReferenceGenericStructTests
{
    private const string LibSource = """
        using MessagePack;
        namespace Lib
        {
            [MessagePackObject]
            public struct Item<T>
            {
                [Key(0)]
                public string Key;
            }
        }
        """;

    private const string AppSource = """
        using MessagePack;
        using Lib;
        namespace App
        {
            [MessagePackObject]
            public sealed class Cmd
            {
                [Key(0)]
                public string Scope;

                [Key(1)]
                public Item<int> Member;
            }

            [GeneratedMessagePackResolver]
            partial class MyResolver
            {
            }
        }
        """;

    [Fact]
    public async Task GeneratedOutputIsSameForImageAndCompilationReference()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> baseRefs = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest);

        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(LibSource, parseOptions) },
            baseRefs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using MemoryStream ms = new MemoryStream();
        var emit = lib.Emit(ms);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        MetadataReference imageRef = MetadataReference.CreateFromImage(ms.ToArray());
        MetadataReference compilationRef = lib.ToMetadataReference();

        string[] fromImage = Run(baseRefs, imageRef, parseOptions);
        string[] fromCompilation = Run(baseRefs, compilationRef, parseOptions);

        Assert.Equal(fromImage, fromCompilation);
    }

    private static string[] Run(List<MetadataReference> baseRefs, MetadataReference libRef, CSharpParseOptions parseOptions)
    {
        List<MetadataReference> refs = new List<MetadataReference>(baseRefs) { libRef };
        CSharpCompilation app = CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource, parseOptions) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGenerators(app);
        GeneratorDriverRunResult result = driver.GetRunResult();
        GeneratorRunResult gen = Assert.Single(result.Results);
        Assert.Null(gen.Exception);
        return gen.GeneratedSources
            .OrderBy(s => s.HintName, StringComparer.Ordinal)
            .Select(s => s.HintName + "\n" + s.SourceText.ToString())
            .ToArray();
    }
}
