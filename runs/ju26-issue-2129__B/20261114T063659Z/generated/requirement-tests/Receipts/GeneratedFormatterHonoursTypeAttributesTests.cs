// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterHonoursTypeAttributesTests
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp12);

    private const string ProjectBSource = """
        using MessagePack;
        using MessagePack.Formatters;

        namespace ProjectB
        {
            [GeneratedMessagePackFormatter]
            partial class MyTypeFormatter : IMessagePackFormatter<ProjectA.MyType>;

            [GeneratedMessagePackResolver]
            partial class MyResolver
            {
            }
        }
        """;

    [Fact]
    public async Task IgnoreMemberOnReferencedMessagePackObjectIsHonoured()
    {
        string projectA = """
            using MessagePack;

            namespace ProjectA
            {
                [MessagePackObject]
                public class MyType
                {
                    [Key(0)]
                    public int Id { get; set; }

                    [IgnoreMember]
                    public string SecretValue { get; set; }
                }
            }
            """;

        string generated = await GenerateAsync(projectA, ProjectBSource);
        Assert.Contains("MyTypeFormatter", generated);
        Assert.DoesNotContain("SecretValue", generated);
    }

    [Fact]
    public async Task DataContractOnReferencedTypeIsHonoured()
    {
        string projectA = """
            using System.Runtime.Serialization;

            namespace ProjectA
            {
                [DataContract]
                public class MyType
                {
                    [DataMember(Name = "id")]
                    public int Id { get; set; }

                    public string SecretValue { get; set; }
                }
            }
            """;

        string generated = await GenerateAsync(projectA, ProjectBSource);
        Assert.Contains("MyTypeFormatter", generated);
        Assert.DoesNotContain("SecretValue", generated);
    }

    private static async Task<string> GenerateAsync(string projectASource, string projectBSource)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        MetadataReference[] messagePack =
        {
            MetadataReference.CreateFromFile(typeof(global::MessagePack.MessagePackObjectAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(global::MessagePack.MessagePackSerializer).Assembly.Location),
        };

        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(projectASource, ParseOptions) },
            framework.AddRange(messagePack),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        var emitResult = projectA.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics.Select(d => d.ToString())));
        MetadataReference projectARef = MetadataReference.CreateFromImage(stream.ToArray());

        CSharpCompilation projectB = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(projectBSource, ParseOptions) },
            framework.AddRange(messagePack).Add(projectARef),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(projectB, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);

        Diagnostic[] errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(d => d.ToString())));

        return string.Join(Environment.NewLine, driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()));
    }
}
