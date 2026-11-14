// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class Req2129AttributesHonouredTests
{
    private static readonly CSharpParseOptions ParseOpts = new(LanguageVersion.CSharp12);

    private static MetadataReference[] MessagePackRefs() => new MetadataReference[]
    {
        MetadataReference.CreateFromFile(typeof(global::MessagePack.MessagePackObjectAttribute).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(global::MessagePack.MessagePackSerializer).Assembly.Location),
    };

    private static MetadataReference EmitProjectA(string source, IEnumerable<MetadataReference> refs)
    {
        CSharpCompilation a = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(source, ParseOpts) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream ms = new();
        var result = a.Emit(ms);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return MetadataReference.CreateFromImage(ms.ToArray());
    }

    private static string GenerateFor(string typeName, IEnumerable<MetadataReference> refs)
    {
        string sourceB = $$"""
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB;

            [GeneratedMessagePackFormatter]
            partial class TaggedFormatter : IMessagePackFormatter<{{typeName}}>;

            [GeneratedMessagePackResolver]
            partial class MyResolver { }
            """;

        CSharpCompilation b = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(sourceB, ParseOpts) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new MessagePackGenerator().AsSourceGenerator() }, parseOptions: ParseOpts);
        driver = driver.RunGeneratorsAndUpdateCompilation(b, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);

        Diagnostic[] errors = generatorDiagnostics.Concat(output.GetDiagnostics())
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(e => e.ToString())));

        string generated = string.Join("\n", driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()));
        Assert.Contains("TaggedFormatter", generated);
        return generated;
    }

    [Fact]
    public async Task HonoursIgnoreMemberOnMessagePackObject()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        MetadataReference[] mp = MessagePackRefs();

        MetadataReference projectA = EmitProjectA(
            """
            using MessagePack;

            namespace ProjectA
            {
                [MessagePackObject]
                public class Tagged
                {
                    [Key(0)]
                    public int Id { get; set; }

                    [IgnoreMember]
                    public string Secret { get; set; }
                }
            }
            """,
            framework.Concat(mp));

        string generated = GenerateFor("ProjectA.Tagged", framework.Concat(mp).Append(projectA));
        Assert.DoesNotContain("Secret", generated);
    }

    [Fact]
    public async Task HonoursDataContractMembership()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);

        MetadataReference projectA = EmitProjectA(
            """
            using System.Runtime.Serialization;

            namespace ProjectA
            {
                [DataContract]
                public class Tagged
                {
                    [DataMember]
                    public int Id { get; set; }

                    public string Secret { get; set; }
                }
            }
            """,
            framework);

        string generated = GenerateFor("ProjectA.Tagged", framework.Concat(MessagePackRefs()).Append(projectA));
        Assert.DoesNotContain("Secret", generated);
    }
}
