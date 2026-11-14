// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public sealed class GeneratedFormatterCollectorReceiptTests
{
    [Fact]
    public async Task ContractlessGeneration_IncludesMutableFieldsAndSkipsReadOnlyFields()
    {
        const string source = """
            using MessagePack;
            using MessagePack.Formatters;

            public class MyObject
            {
                public int Value;
                public readonly int ReadOnly;
            }

            [GeneratedMessagePackFormatter]
            public partial class MyObjectFormatter : IMessagePackFormatter<MyObject> { }
            """;

        GeneratedResult result = await GenerateAsync(source);
        AssertNoCompilationErrors(result.Compilation);
        string formatter = GetFormatterSource(result, "MyObjectFormatter");
        formatter.Should().Contain("Value");
        formatter.Should().NotContain("ReadOnly");
    }

    [Fact]
    public async Task MessagePackAttributesOnReferencedType_AreHonoured()
    {
        const string projectASource = """
            using MessagePack;

            namespace ProjectA;

            [MessagePackObject]
            public class MyObject
            {
                [Key(0)]
                public int Value { get; set; }

                [IgnoreMember]
                public int Ignored { get; set; }
            }
            """;
        MetadataReference projectA = await CompileToMetadataReferenceAsync(projectASource, "ProjectA");
        const string projectBSource = """
            using MessagePack;
            using MessagePack.Formatters;

            [GeneratedMessagePackFormatter]
            public partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject> { }
            """;

        GeneratedResult result = await GenerateAsync(projectBSource, projectA);
        AssertNoCompilationErrors(result.Compilation);
        string formatter = GetFormatterSource(result, "MyObjectFormatter");
        formatter.Should().Contain("WriteArrayHeader");
        formatter.Should().NotContain("Ignored");
    }

    private static async Task<MetadataReference> CompileToMetadataReferenceAsync(string source, string assemblyName)
    {
        var references = (await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None))
            .Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack))
            .ToList();
        CSharpCompilation compilation = CreateCompilation(source, assemblyName, references);
        AssertNoCompilationErrors(compilation);
        using MemoryStream stream = new();
        var emit = compilation.Emit(stream);
        emit.Success.Should().BeTrue(string.Join(Environment.NewLine, emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static async Task<GeneratedResult> GenerateAsync(string source, MetadataReference? additionalReference = null)
    {
        var references = (await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None))
            .Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack))
            .ToList();
        if (additionalReference is not null)
        {
            references.Add(additionalReference);
        }

        CSharpCompilation compilation = CreateCompilation(source, "ProjectB", references);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
        return new GeneratedResult(output, driver);
    }

    private static CSharpCompilation CreateCompilation(string source, string assemblyName, IEnumerable<MetadataReference> references)
    {
        return CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    private static string GetFormatterSource(GeneratedResult result, string formatterName)
    {
        return result.Driver.GetRunResult().Results
            .SelectMany(r => r.GeneratedSources)
            .Single(s => s.SourceText.ToString().Contains($"partial class {formatterName}"))
            .SourceText.ToString();
    }

    private static void AssertNoCompilationErrors(Compilation compilation)
    {
        string errors = string.Join(Environment.NewLine, compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        errors.Should().BeEmpty();
    }

    private sealed record GeneratedResult(Compilation Compilation, GeneratorDriver Driver);
}
