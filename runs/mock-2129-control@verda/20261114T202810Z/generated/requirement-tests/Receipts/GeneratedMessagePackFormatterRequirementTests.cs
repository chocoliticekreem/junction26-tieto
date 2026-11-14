// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedMessagePackFormatterRequirementTests
{
    [Fact]
    public async Task GeneratesContractlessFormatterForAttributeFreeMetadataType()
    {
        string projectASource = """
            namespace ProjectA;

            public class MyObject
            {
                public int Id { get; set; }

                public string? Name { get; set; }

                public int Computed => this.Id;

                private string Secret { get; set; } = string.Empty;
            }
            """;

        string formatter = await GenerateFormatterAsync(projectASource, projectAReferencesMessagePack: false);

        Assert.Contains("partial class MyObjectFormatter", formatter);
        Assert.Contains("WriteMapHeader", formatter);
        Assert.Contains("value.Id", formatter);
        Assert.Contains("value.Name", formatter);
        Assert.DoesNotContain("Computed", formatter);
        Assert.DoesNotContain("Secret", formatter);
    }

    [Fact]
    public async Task HonorsMessagePackAttributesOnMetadataType()
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
                public string? Secret { get; set; }
            }
            """;

        string formatter = await GenerateFormatterAsync(projectASource, projectAReferencesMessagePack: true);

        Assert.Contains("partial class MyObjectFormatter", formatter);
        Assert.Contains("WriteArrayHeader", formatter);
        Assert.Contains("value.Id", formatter);
        Assert.DoesNotContain("WriteMapHeader", formatter);
        Assert.DoesNotContain("Secret", formatter);
    }

    private static async Task<string> GenerateFormatterAsync(string projectASource, bool projectAReferencesMessagePack)
    {
        ImmutableArray<MetadataReference> frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> projectAReferences = new(frameworkReferences);
        if (projectAReferencesMessagePack)
        {
            projectAReferences.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        }

        CSharpParseOptions parseOptions = new(LanguageVersion.CSharp12);
        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(projectASource, parseOptions) },
            projectAReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        AssertNoErrors(projectA.GetDiagnostics());

        const string projectBSource = """
            using MessagePack;
            using MessagePack.Formatters;

            [GeneratedMessagePackFormatter]
            public partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject>
            {
            }

            [GeneratedMessagePackResolver]
            public partial class GeneratedResolver
            {
            }
            """;

        List<MetadataReference> projectBReferences = new(frameworkReferences);
        projectBReferences.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        projectBReferences.Add(projectA.ToMetadataReference());
        CSharpCompilation projectB = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(projectBSource, parseOptions) },
            projectBReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePackGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(projectB, out Compilation outputCompilation, out ImmutableArray<Diagnostic> generatorDiagnostics);
        AssertNoErrors(generatorDiagnostics);
        AssertNoErrors(outputCompilation.GetDiagnostics());

        string? formatter = driver.GetRunResult().Results
            .SelectMany(result => result.GeneratedSources)
            .Select(source => source.SourceText.ToString())
            .SingleOrDefault(source => source.Contains("partial class MyObjectFormatter", StringComparison.Ordinal));
        Assert.NotNull(formatter);
        return formatter!;
    }

    private static void AssertNoErrors(IEnumerable<Diagnostic> diagnostics)
    {
        Diagnostic[] errors = diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(error => error.ToString())));
    }
}
