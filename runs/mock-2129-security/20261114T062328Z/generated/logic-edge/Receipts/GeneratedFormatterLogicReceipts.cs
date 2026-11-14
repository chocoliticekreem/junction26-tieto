// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterLogicReceipts
{
    private sealed class CustomKey
    {
        public int Value { get; set; }
    }

    [Fact]
    public void UntrustedData_RejectsNonHashResistantKeyType()
    {
        Assert.Throws<TypeAccessException>(() => global::MessagePack.MessagePackSecurity.UntrustedData.GetEqualityComparer<CustomKey>());
    }

    [Fact]
    public async Task GeneratedFormatter_HonoursIgnoreMemberOnFormattedType()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> refs = framework.Concat<MetadataReference>(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        CSharpParseOptions parseOptions = new(LanguageVersion.Latest);

        string libSource = """
            namespace Lib
            {
                [MessagePack.MessagePackObject]
                public class MyType
                {
                    [MessagePack.Key(0)]
                    public int Visible { get; set; }

                    [MessagePack.IgnoreMember]
                    public string IgnoredSecret { get; set; }
                }
            }
            """;

        CSharpCompilation lib = CSharpCompilation.Create(
            "ReceiptsLib",
            new[] { CSharpSyntaxTree.ParseText(libSource, parseOptions) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        var emitResult = lib.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference libRef = MetadataReference.CreateFromImage(stream.ToArray());

        string appSource = """
            using MessagePack;
            using MessagePack.Formatters;

            [GeneratedMessagePackResolver]
            partial class MyResolver { }

            [GeneratedMessagePackFormatter]
            partial class LibFormatter : IMessagePackFormatter<Lib.MyType> { }
            """;

        CSharpCompilation app = CSharpCompilation.Create(
            "ReceiptsApp",
            new[] { CSharpSyntaxTree.ParseText(appSource, parseOptions) },
            refs.Append(libRef),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGenerators(app);
        GeneratorDriverRunResult result = driver.GetRunResult();
        string all = string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));

        Assert.Contains("LibFormatter", all);
        Assert.DoesNotContain("IgnoredSecret", all);
    }
}
