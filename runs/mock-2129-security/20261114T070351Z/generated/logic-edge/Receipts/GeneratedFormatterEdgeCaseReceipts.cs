// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterEdgeCaseReceipts
{
    [Fact]
    public void UntrustedData_RejectsNonHashResistantKeyType()
    {
        Assert.Throws<TypeAccessException>(() => global::MessagePack.MessagePackSecurity.UntrustedData.GetEqualityComparer<CustomKey>());
    }

    [Fact]
    public async Task GeneratedFormatter_HonoursIgnoreMemberOnFormattedType()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = new(framework);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpParseOptions parseOptions = new(LanguageVersion.Latest);

        string sourceA = """
            using MessagePack;
            namespace ProjectA
            {
                [MessagePackObject]
                public class MyType
                {
                    [Key(0)]
                    public int Value { get; set; }

                    [IgnoreMember]
                    public string SecretToken { get; set; }
                }
            }
            """;

        CSharpCompilation compA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(sourceA, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using MemoryStream ms = new();
        var emitResult = compA.Emit(ms);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference refA = MetadataReference.CreateFromImage(ms.ToArray());

        string sourceB = """
            using MessagePack;
            using MessagePack.Formatters;
            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                public partial class MyTypeFormatter : IMessagePackFormatter<ProjectA.MyType> { }

                [GeneratedMessagePackResolver]
                partial class MyResolver { }
            }
            """;

        List<MetadataReference> referencesB = new(references) { refA };
        CSharpCompilation compB = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(sourceB, parseOptions) },
            referencesB,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGenerators(compB);

        List<string> generated = driver.GetRunResult().GeneratedTrees.Select(t => t.GetText().ToString()).ToList();
        List<string> formatterSources = generated.Where(s => s.Contains("class MyTypeFormatter")).ToList();

        Assert.NotEmpty(formatterSources);
        foreach (string text in formatterSources)
        {
            Assert.DoesNotContain("SecretToken", text);
        }
    }

    public class CustomKey
    {
    }
}
