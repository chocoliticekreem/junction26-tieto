// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedMessagePackFormatterReceiptTests
{
    [Fact]
    public async Task FormatterIsGeneratedForAttributeFreeTypeInReferencedAssembly()
    {
        const string projectASource = """
            #nullable enable
            namespace ProjectA;

            public class MyObject
            {
                public int Id { get; set; }

                public string? Name { get; set; }
            }
            """;

        string formatter = await GenerateFormatterSourceAsync(projectASource, includeMessagePackInProjectA: false);

        formatter.Should().Contain("partial class MyObjectFormatter");
        formatter.Should().Contain(".Id");
        formatter.Should().Contain(".Name");
    }

    [Fact]
    public async Task MessagePackAttributesOnReferencedTypeAreHonored()
    {
        const string projectASource = """
            #nullable enable
            using MessagePack;

            namespace ProjectA;

            [MessagePackObject]
            public class MyObject
            {
                [Key(0)]
                public int Id { get; set; }

                [IgnoreMember]
                public int Ignored { get; set; }
            }
            """;

        string formatter = await GenerateFormatterSourceAsync(projectASource, includeMessagePackInProjectA: true);

        formatter.Should().Contain("WriteArrayHeader");
        formatter.Should().NotContain("WriteMapHeader");
        formatter.Should().Contain(".Id");
        formatter.Should().NotContain(".Ignored");
    }

    [Fact]
    public async Task BodylessPartialFormatterClassIsSupported()
    {
        const string projectASource = """
            namespace ProjectA;

            public class MyObject
            {
                public int Id { get; set; }
            }
            """;

        string formatter = await GenerateFormatterSourceAsync(projectASource, includeMessagePackInProjectA: false, bodyless: true);

        formatter.Should().Contain("partial class MyObjectFormatter");
        formatter.Should().Contain(".Id");
    }

    private static async Task<string> GenerateFormatterSourceAsync(string projectASource, bool includeMessagePackInProjectA, bool bodyless = false)
    {
        ImmutableArray<MetadataReference> frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        ImmutableArray<MetadataReference> projectAReferences = frameworkReferences;
        if (includeMessagePackInProjectA)
        {
            projectAReferences = projectAReferences.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        }

        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(projectASource, parseOptions) },
            projectAReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var projectAImage = new MemoryStream();
        var projectAEmit = projectA.Emit(projectAImage);
        projectAEmit.Success.Should().BeTrue(string.Join(Environment.NewLine, projectAEmit.Diagnostics));
        MetadataReference projectAReference = MetadataReference.CreateFromImage(projectAImage.ToArray());

        string formatterBody = bodyless ? ";" : "{ }";
        string projectBSource = $$"""
            #nullable enable
            using MessagePack;
            using MessagePack.Formatters;

            [GeneratedMessagePackFormatter]
            public partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject> {{formatterBody}}

            [GeneratedMessagePackResolver]
            public partial class ReceiptResolver { }
            """;

        ImmutableArray<MetadataReference> projectBReferences = frameworkReferences
            .AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack))
            .Add(projectAReference);
        var projectB = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(projectBSource, parseOptions) },
            projectBReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(projectB, out Compilation outputCompilation, out ImmutableArray<Diagnostic> generatorDiagnostics);
        Diagnostic[] errors = generatorDiagnostics
            .Concat(outputCompilation.GetDiagnostics())
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        errors.Should().BeEmpty(string.Join(Environment.NewLine, errors.Select(error => error.ToString())));

        string[] formatterSources = driver.GetRunResult().Results
            .SelectMany(result => result.GeneratedSources)
            .Select(source => source.SourceText.ToString())
            .Where(text => text.Contains("partial class MyObjectFormatter", StringComparison.Ordinal))
            .ToArray();
        formatterSources.Should().ContainSingle();
        return formatterSources[0];
    }
}
