// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterHonoursAttributesTests
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp12);

    private const string ProjectASource = """
        using MessagePack;

        namespace ProjectA
        {
            [MessagePackObject]
            public class Keyed
            {
                [Key(0)]
                public int Id { get; set; }

                [Key(1)]
                public string Label { get; set; }

                [IgnoreMember]
                public string IgnoredSecretXyz { get; set; }
            }
        }
        """;

    private const string ProjectBSource = """
        using MessagePack;
        using MessagePack.Formatters;

        namespace ProjectB
        {
            [GeneratedMessagePackFormatter]
            partial class KeyedFormatter : IMessagePackFormatter<global::ProjectA.Keyed>;

            [GeneratedMessagePackResolver]
            partial class MyResolver { }
        }
        """;

    [Fact]
    public async Task IgnoreMemberOnReferencedTypeIsHonoured()
    {
        string formatterSource = await GenerateFormatterSourceAsync();
        Assert.DoesNotContain("IgnoredSecretXyz", formatterSource);
    }

    [Fact]
    public async Task IntKeysOnReferencedTypeProduceArrayFormat()
    {
        string formatterSource = await GenerateFormatterSourceAsync();
        Assert.Contains("WriteArrayHeader", formatterSource);
        Assert.DoesNotContain("WriteMapHeader", formatterSource);
    }

    private static async Task<string> GenerateFormatterSourceAsync()
    {
        List<MetadataReference> projectAReferences = await GetReferencesAsync();
        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(ProjectASource, ParseOptions) },
            projectAReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        Microsoft.CodeAnalysis.Emit.EmitResult emitResult = projectA.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics.Select(d => d.ToString())));

        List<MetadataReference> projectBReferences = await GetReferencesAsync();
        projectBReferences.Add(MetadataReference.CreateFromImage(stream.ToArray()));
        CSharpCompilation projectB = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(ProjectBSource, ParseOptions) },
            projectBReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(projectB, out Compilation output, out _);
        GeneratorDriverRunResult run = driver.GetRunResult();

        Diagnostic[] errors = output.GetDiagnostics().Concat(run.Diagnostics).Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(d => d.ToString())));

        string formatterSource = string.Join("\n", run.GeneratedTrees.Select(t => t.ToString()).Where(t => t.Contains("class KeyedFormatter")));
        Assert.False(string.IsNullOrEmpty(formatterSource), "No generated source declares KeyedFormatter.");
        return formatterSource;
    }

    private static async Task<List<MetadataReference>> GetReferencesAsync()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = new(framework);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        return references;
    }
}
