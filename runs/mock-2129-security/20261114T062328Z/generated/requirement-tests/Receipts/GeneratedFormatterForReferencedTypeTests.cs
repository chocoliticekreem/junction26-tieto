// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterForReferencedTypeTests
{
    private const string ProjectASource = """
        namespace ProjectA
        {
            public class MyType
            {
                private int hiddenCounter;

                public int Id { get; set; }

                public string Name { get; set; }

                public int GetHidden() => hiddenCounter++;
            }
        }
        """;

    [Fact]
    public async Task BodylessFormatterForReferencedTypeWithoutAttributesCompiles()
    {
        string projectB = """
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

        (GeneratorDriverRunResult run, Compilation output) = await RunAsync(projectB);

        Assert.All(run.Results, r => Assert.Null(r.Exception));
        string formatter = FindFormatterSource(run);
        Assert.Contains("MyTypeFormatter", formatter);
        Diagnostic[] errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(e => e.ToString())));
    }

    [Fact]
    public async Task ContractlessFormatterCoversPublicMembersOnly()
    {
        string projectB = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                public partial class MyTypeFormatter : IMessagePackFormatter<ProjectA.MyType>
                {
                }

                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }
            }
            """;

        (GeneratorDriverRunResult run, Compilation output) = await RunAsync(projectB);

        Assert.All(run.Results, r => Assert.Null(r.Exception));
        string formatter = FindFormatterSource(run);
        Assert.Contains("Id", formatter);
        Assert.Contains("Name", formatter);
        Assert.DoesNotContain("hiddenCounter", formatter);
        Diagnostic[] errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(e => e.ToString())));
    }

    private static string FindFormatterSource(GeneratorDriverRunResult run)
    {
        string[] sources = run.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => s.SourceText.ToString())
            .Where(t => t.Contains("class MyTypeFormatter"))
            .ToArray();
        Assert.True(sources.Length > 0, "No generated source declares class MyTypeFormatter.");
        return string.Join(Environment.NewLine, sources);
    }

    private static async Task<(GeneratorDriverRunResult Run, Compilation Output)> RunAsync(string projectBSource)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        CSharpParseOptions parseOptions = new(LanguageVersion.CSharp12);

        // Project A references only the framework, never MessagePack.
        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(ProjectASource, parseOptions) },
            framework,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        var emitResult = projectA.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics.Select(d => d.ToString())));
        MetadataReference projectARef = MetadataReference.CreateFromImage(stream.ToArray());

        List<MetadataReference> references = Enumerable.Concat<MetadataReference>(framework, ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        references.Add(projectARef);

        CSharpCompilation projectB = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(projectBSource, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(projectB, out Compilation output, out ImmutableArray<Diagnostic> _);
        return (driver.GetRunResult(), output);
    }
}
