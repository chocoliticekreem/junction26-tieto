// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterHonoursAttributesTests
{
    private const string LibSource = """
        using MessagePack;

        namespace ProjectA
        {
            [MessagePackObject]
            public class MyObject
            {
                [Key(0)]
                public int Id { get; set; }

                [Key(1)]
                public string Label { get; set; }

                [IgnoreMember]
                public string Secret { get; set; }
            }
        }
        """;

    private const string AppSource = """
        using MessagePack;
        using MessagePack.Formatters;

        [GeneratedMessagePackResolver]
        partial class MyResolver { }

        [GeneratedMessagePackFormatter]
        partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject>
        {
        }
        """;

    [Fact]
    public async Task AttributedReferencedType_IntKeysProduceArrayFormatter()
    {
        string formatter = await GetFormatterSourceAsync();
        Assert.Contains("WriteArrayHeader", formatter);
        Assert.DoesNotContain("WriteMapHeader", formatter);
    }

    [Fact]
    public async Task AttributedReferencedType_IgnoreMemberIsNotSerialized()
    {
        string formatter = await GetFormatterSourceAsync();
        Assert.DoesNotContain("Secret", formatter);
    }

    private static async Task<string> GetFormatterSourceAsync()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        MetadataReference[] messagePack = ReferencesHelper.GetReferences(ReferencesSet.MessagePack).ToArray();
        CSharpParseOptions parse = new CSharpParseOptions(LanguageVersion.CSharp12);
        CSharpCompilationOptions compOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true);

        CSharpCompilation lib = CSharpCompilation.Create("ProjectA", new[] { CSharpSyntaxTree.ParseText(LibSource, parse) }, framework.Concat(messagePack), compOptions);
        using MemoryStream ms = new MemoryStream();
        var emit = lib.Emit(ms);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        MetadataReference libRef = MetadataReference.CreateFromImage(ms.ToArray());

        CSharpCompilation app = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(AppSource, parse) },
            framework.Concat(messagePack).Append(libRef),
            compOptions);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new MessagePackGenerator().AsSourceGenerator() }, parseOptions: parse);
        driver = driver.RunGeneratorsAndUpdateCompilation(app, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
        Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        string? formatter = driver.GetRunResult().GeneratedTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("MyObjectFormatter") && s.Contains("Serialize("));
        Assert.NotNull(formatter);
        return formatter!;
    }
}
