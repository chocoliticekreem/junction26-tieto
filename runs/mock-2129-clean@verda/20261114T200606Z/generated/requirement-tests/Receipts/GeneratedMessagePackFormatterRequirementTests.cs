// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedMessagePackFormatterRequirementTests
{
    private const string FormatterSource = """
        using MessagePack;
        using MessagePack.Formatters;

        [GeneratedMessagePackFormatter]
        public partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject>;
        """;

    [Fact]
    public async Task BodylessFormatter_GeneratesContractlessFormatterForReferencedType()
    {
        string projectASource = """
            namespace ProjectA;

            public class MyObject
            {
                public int Id { get; set; }

                public string? Name { get; set; }
            }
            """;

        string generated = await GenerateAsync(projectASource, projectAReferencesMessagePack: false);

        Assert.Contains("partial class MyObjectFormatter", generated);
        Assert.Contains("IMessagePackFormatter<global::ProjectA.MyObject>", generated);
        Assert.Contains("Serialize(", generated);
        Assert.Contains("Deserialize(", generated);
        Assert.Contains("writer.WriteMapHeader(2);", generated);
    }

    [Fact]
    public async Task MessagePackAttributesOnReferencedType_AreHonoured()
    {
        string projectASource = """
            using MessagePack;

            namespace ProjectA;

            [MessagePackObject]
            public class MyObject
            {
                [Key(0)]
                public int Id { get; set; }

                [IgnoreMember]
                public string? Ignored { get; set; }
            }
            """;

        string generated = await GenerateAsync(projectASource, projectAReferencesMessagePack: true);

        Assert.Contains("writer.WriteArrayHeader(1);", generated);
        Assert.Contains("writer.Write(0);", generated);
        Assert.Contains("value.Id", generated);
        Assert.DoesNotContain("Ignored", generated);
    }

    private static async Task<string> GenerateAsync(string projectASource, bool projectAReferencesMessagePack)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var compilationOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable);
        ImmutableArray<MetadataReference> frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        ImmutableArray<MetadataReference> projectAReferences = projectAReferencesMessagePack
            ? frameworkReferences.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack))
            : frameworkReferences;

        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(projectASource, parseOptions) },
            projectAReferences,
            compilationOptions);

        using var projectAImage = new MemoryStream();
        var emitResult = projectA.Emit(projectAImage);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        ImmutableArray<MetadataReference> projectBReferences = frameworkReferences
            .AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack))
            .Add(MetadataReference.CreateFromImage(projectAImage.ToArray()));
        CSharpCompilation projectB = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(FormatterSource, parseOptions) },
            projectBReferences,
            compilationOptions);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            projectB,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(outputCompilation.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        GeneratorDriverRunResult runResult = driver.GetRunResult();
        return string.Join(
            Environment.NewLine,
            runResult.Results.SelectMany(result => result.GeneratedSources).Select(source => source.SourceText.ToString()));
    }
}
