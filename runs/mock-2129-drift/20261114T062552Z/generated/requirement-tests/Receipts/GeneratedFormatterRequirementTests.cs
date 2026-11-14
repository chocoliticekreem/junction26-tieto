// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterRequirementTests
{
    private const string ProjectASource = """
        namespace ProjectA
        {
            public class MyObject
            {
                public int Id { get; set; }
                public string Name { get; set; }
            }
        }
        """;

    [Fact]
    public async Task FormatterForTypeInReferencedAssemblyWithoutMessagePack_IsGenerated()
    {
        List<MetadataReference> frameworkRefs = await GetFrameworkReferencesAsync();
        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(ProjectASource) },
            frameworkRefs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        var emitResult = projectA.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference projectARef = MetadataReference.CreateFromImage(stream.ToArray());

        string projectBSource = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject>;

                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }
            }
            """;

        (string generated, List<Diagnostic> generatorErrors, List<Diagnostic> outputErrors) = await RunGeneratorAsync(projectBSource, projectARef);

        Assert.True(generatorErrors.Count == 0, string.Join(Environment.NewLine, generatorErrors));
        Assert.Contains("partial class MyObjectFormatter", generated);
        Assert.True(outputErrors.Count == 0, string.Join(Environment.NewLine, outputErrors));
    }

    [Fact]
    public async Task FormatterHonoursDataContractOnFormattedType()
    {
        string source = """
            using System.Runtime.Serialization;
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [DataContract]
                public class MyObject
                {
                    [DataMember(Order = 0)]
                    public int Id { get; set; }

                    public string Secret { get; set; }
                }

                [GeneratedMessagePackFormatter]
                partial class MyObjectFormatter : IMessagePackFormatter<MyObject>;

                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }
            }
            """;

        (string generated, List<Diagnostic> generatorErrors, List<Diagnostic> outputErrors) = await RunGeneratorAsync(source, null);

        Assert.True(generatorErrors.Count == 0, string.Join(Environment.NewLine, generatorErrors));
        Assert.Contains("partial class MyObjectFormatter", generated);
        Assert.DoesNotContain("Secret", generated);
        Assert.True(outputErrors.Count == 0, string.Join(Environment.NewLine, outputErrors));
    }

    private static async Task<List<MetadataReference>> GetFrameworkReferencesAsync()
    {
        List<MetadataReference> refs = new();
        foreach (MetadataReference r in await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None))
        {
            refs.Add(r);
        }

        return refs;
    }

    private static async Task<(string Generated, List<Diagnostic> GeneratorErrors, List<Diagnostic> OutputErrors)> RunGeneratorAsync(string source, MetadataReference? extraReference)
    {
        List<MetadataReference> refs = await GetFrameworkReferencesAsync();
        foreach (var r in ReferencesHelper.GetReferences(ReferencesSet.MessagePack))
        {
            refs.Add(r);
        }

        if (extraReference is not null)
        {
            refs.Add(extraReference);
        }

        CSharpParseOptions parseOptions = new(LanguageVersion.CSharp12);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> _);
        GeneratorDriverRunResult result = driver.GetRunResult();

        string generated = string.Join(
            "\n",
            result.Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()));
        List<Diagnostic> generatorErrors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        List<Diagnostic> outputErrors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        return (generated, generatorErrors, outputErrors);
    }
}
