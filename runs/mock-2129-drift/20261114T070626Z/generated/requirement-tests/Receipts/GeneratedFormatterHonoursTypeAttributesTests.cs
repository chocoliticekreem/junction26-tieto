// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterHonoursTypeAttributesTests(ITestOutputHelper logger)
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp12);

    [Fact]
    public async Task DataContractMembershipIsHonoured()
    {
        string source = """
            using System.Runtime.Serialization;
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [DataContract]
                public class MyType
                {
                    [DataMember(Name = "id")]
                    public int Id { get; set; }

                    public string Secret { get; set; }
                }

                [GeneratedMessagePackFormatter]
                public partial class CustomMyTypeFormatter : IMessagePackFormatter<MyType>
                {
                }

                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }
            }
            """;
        await this.AssertSecretNotSerializedAsync(source);
    }

    [Fact]
    public async Task MessagePackIgnoreMemberIsHonoured()
    {
        string source = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [MessagePackObject]
                public class MyType
                {
                    [Key(0)]
                    public int Id { get; set; }

                    [IgnoreMember]
                    public string Secret { get; set; }
                }

                [GeneratedMessagePackFormatter]
                public partial class CustomMyTypeFormatter : IMessagePackFormatter<MyType>
                {
                }

                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }
            }
            """;
        await this.AssertSecretNotSerializedAsync(source);
    }

    private async Task AssertSecretNotSerializedAsync(string source)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = new(framework);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        GeneratorDriverRunResult runResult = driver.GetRunResult();

        foreach (SyntaxTree tree in runResult.GeneratedTrees)
        {
            logger.WriteLine(tree.FilePath);
            logger.WriteLine(tree.ToString());
        }

        foreach (Diagnostic diagnostic in generatorDiagnostics)
        {
            logger.WriteLine(diagnostic.ToString());
        }

        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(runResult.GeneratedTrees, t =>
        {
            string text = t.ToString();
            return text.Contains("CustomMyTypeFormatter") && text.Contains("Deserialize");
        });
        Assert.DoesNotContain(runResult.GeneratedTrees, t => t.ToString().Contains("Secret"));
    }
}
