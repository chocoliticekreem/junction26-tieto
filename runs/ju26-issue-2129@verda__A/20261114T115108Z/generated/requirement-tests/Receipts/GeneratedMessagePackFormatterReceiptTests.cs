// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedMessagePackFormatterReceiptTests
{
    [Fact]
    public async Task ContractlessFormatterForAttributeFreeReferencedTypeIsGenerated()
    {
        MetadataReference projectA = await CreateReferencedAssemblyAsync(
            """
            namespace ProjectA;

            public class MyObject
            {
                public int Id { get; set; }

                public string? Name { get; set; }

                public int ReadOnly => this.Id;
            }
            """,
            includeMessagePack: false);

        GeneratorDriverRunResult runResult = await RunGeneratorAsync(
            """
            using MessagePack;
            using MessagePack.Formatters;

            [GeneratedMessagePackFormatter]
            public partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject>;

            [GeneratedMessagePackResolver]
            public partial class ProjectBResolver;
            """,
            projectA);

        string formatter = GetGeneratedFormatterSource(runResult, "MyObjectFormatter");
        Assert.Contains("partial class MyObjectFormatter", formatter);
        Assert.Contains("WriteMapHeader(2)", formatter);
        Assert.Contains("Id", formatter);
        Assert.Contains("Name", formatter);
        Assert.DoesNotContain("ReadOnly", formatter);
    }

    [Fact]
    public async Task MessagePackAttributesOnReferencedTypeAreHonored()
    {
        MetadataReference projectA = await CreateReferencedAssemblyAsync(
            """
            using MessagePack;

            namespace ProjectA;

            [MessagePackObject]
            public class AttributedObject
            {
                [Key(0)]
                public int Id { get; set; }

                [IgnoreMember]
                public string? Secret { get; set; }
            }
            """,
            includeMessagePack: true);

        GeneratorDriverRunResult runResult = await RunGeneratorAsync(
            """
            using MessagePack;
            using MessagePack.Formatters;

            [GeneratedMessagePackFormatter]
            public partial class AttributedObjectFormatter : IMessagePackFormatter<ProjectA.AttributedObject>;

            [GeneratedMessagePackResolver]
            public partial class ProjectBResolver;
            """,
            projectA);

        string formatter = GetGeneratedFormatterSource(runResult, "AttributedObjectFormatter");
        Assert.Contains("WriteArrayHeader(1)", formatter);
        Assert.DoesNotContain("Secret", formatter);
    }

    private static async Task<MetadataReference> CreateReferencedAssemblyAsync(string source, bool includeMessagePack)
    {
        ImmutableArray<MetadataReference> references = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        if (includeMessagePack)
        {
            references = references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        }

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: includeMessagePack ? "ProjectAWithAttributes" : "ProjectA",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview)) },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        using MemoryStream peStream = new();
        Microsoft.CodeAnalysis.Emit.EmitResult emitResult = compilation.Emit(peStream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics.Select(d => d.ToString())));
        return MetadataReference.CreateFromImage(peStream.ToArray().ToImmutableArray());
    }

    private static async Task<GeneratorDriverRunResult> RunGeneratorAsync(string source, MetadataReference projectA)
    {
        CSharpParseOptions parseOptions = new(LanguageVersion.Preview);
        ImmutableArray<MetadataReference> references = (await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None))
            .AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack))
            .Add(projectA);

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: "ProjectB",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out ImmutableArray<Diagnostic> generatorDiagnostics);

        AssertNoErrors(generatorDiagnostics);
        AssertNoErrors(outputCompilation.GetDiagnostics());
        return driver.GetRunResult();
    }

    private static string GetGeneratedFormatterSource(GeneratorDriverRunResult runResult, string formatterName)
    {
        return runResult.Results
            .SelectMany(result => result.GeneratedSources)
            .Select(source => source.SourceText.ToString())
            .Single(text => text.Contains("partial class " + formatterName, StringComparison.Ordinal));
    }

    private static void AssertNoErrors(IEnumerable<Diagnostic> diagnostics)
    {
        Diagnostic[] errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
    }
}
