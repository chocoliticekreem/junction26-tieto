// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ReferencedGenericTypeArgumentTests(ITestOutputHelper logger)
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
        using Lib;
        namespace App
        {
            public enum Color { Red, Green }

            [MessagePackObject]
            public class Cmd
            {
                [Key(0)]
                public Item<Color> Member { get; set; }
            }

            [GeneratedMessagePackResolver]
            partial class R { }
        }
        """;

    [Fact]
    public async Task LocalEnumTypeArgumentOfReferencedGenericClassStillGetsFormatter()
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();

        var lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(LibSource, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var ms = new MemoryStream();
        var emit = lib.Emit(ms);
        Assert.True(emit.Success, string.Join("\n", emit.Diagnostics));
        var libRef = MetadataReference.CreateFromImage(ms.ToArray());

        var app = CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource, parseOptions) },
            references.Append(libRef),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(app, out _, out _);
        var result = driver.GetRunResult();

        foreach (var r in result.Results)
        {
            Assert.Null(r.Exception);
        }

        var generated = result.Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()).ToList();
        foreach (var text in generated)
        {
            logger.WriteLine(text);
        }

        Assert.Contains(generated, t => t.Contains("IMessagePackFormatter<global::App.Color>"));
    }
}
