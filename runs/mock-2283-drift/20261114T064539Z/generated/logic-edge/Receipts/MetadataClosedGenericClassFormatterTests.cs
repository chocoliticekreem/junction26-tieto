// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using FluentAssertions;
using MessagePack.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class MetadataClosedGenericClassFormatterTests
{
    private const string LibSource = """
        using MessagePack;

        namespace Lib
        {
            [MessagePackObject]
            public class Item<T>
            {
                [Key(0)]
                public T Value { get; set; }
            }
        }
        """;

    private const string AppSource = """
        using MessagePack;

        namespace App
        {
            [GeneratedMessagePackResolver]
            partial class MyResolver
            {
            }

            [MessagePackObject]
            public class Cmd
            {
                [Key(0)]
                public string Scope { get; set; }

                [Key(1)]
                public Lib.Item<int> Member { get; set; }
            }
        }
        """;

    [Fact]
    public async Task ClosedGenericClassFromReferencedAssembly_StillGetsFormatterInConsumingAssembly()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = [.. framework, .. ReferencesHelper.GetReferences(ReferencesSet.MessagePack)];

        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.CSharp12);
        CSharpCompilationOptions compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);

        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(LibSource, parseOptions) },
            references,
            compilationOptions);

        using MemoryStream ms = new MemoryStream();
        var emitResult = lib.Emit(ms);
        emitResult.Success.Should().BeTrue(string.Join(Environment.NewLine, emitResult.Diagnostics.Select(d => d.ToString())));
        MetadataReference libReference = MetadataReference.CreateFromImage(ms.ToArray());

        List<MetadataReference> appReferences = [.. references, libReference];
        CSharpCompilation app = CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource, parseOptions) },
            appReferences,
            compilationOptions);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(app, out Compilation _, out ImmutableArray<Diagnostic> _);
        GeneratorDriverRunResult result = driver.GetRunResult();

        foreach (GeneratorRunResult generatorResult in result.Results)
        {
            generatorResult.Exception.Should().BeNull();
        }

        string allGenerated = string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));

        // On base the consuming assembly generated (and registered) a formatter for the referenced closed generic class Lib.Item<T>.
        allGenerated.Should().Contain("ItemFormatter");
    }
}
