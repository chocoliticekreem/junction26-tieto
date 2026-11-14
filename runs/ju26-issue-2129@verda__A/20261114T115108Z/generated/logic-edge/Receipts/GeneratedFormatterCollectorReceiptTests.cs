// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterCollectorReceiptTests
{
    [Fact]
    public async Task MessagePackObjectKeysOnMetadataTypeAreHonored()
    {
        MetadataReference library = await CompileLibraryAsync("""
            using MessagePack;

            [MessagePackObject]
            public class MyObject
            {
                [Key(0)]
                public int Value { get; set; }
            }
            """);

        (Compilation output, GeneratorDriverRunResult result) = await RunGeneratorAsync(
            """
            using MessagePack;
            using MessagePack.Formatters;

            [GeneratedMessagePackFormatter]
            public partial class MyFormatter : IMessagePackFormatter<MyObject>
            {
            }
            """,
            library);

        AssertNoErrors(output);
        string formatter = GetFormatterSource(result);
        Assert.Contains("WriteArrayHeader", formatter);
        Assert.DoesNotContain("WriteMapHeader", formatter);
    }

    [Fact]
    public async Task ExplicitCallbacksInheritedFromBaseTypeRequireCast()
    {
        MetadataReference library = await CompileLibraryAsync("""
            using MessagePack;

            public class BaseObject : IMessagePackSerializationCallbackReceiver
            {
                public int Value { get; set; }

                void IMessagePackSerializationCallbackReceiver.OnBeforeSerialize()
                {
                }

                void IMessagePackSerializationCallbackReceiver.OnAfterDeserialize()
                {
                }
            }

            public class MyObject : BaseObject
            {
            }
            """);

        (Compilation output, GeneratorDriverRunResult _) = await RunGeneratorAsync(
            """
            using MessagePack;
            using MessagePack.Formatters;

            [GeneratedMessagePackFormatter]
            public partial class MyFormatter : IMessagePackFormatter<MyObject>
            {
            }
            """,
            library);

        AssertNoErrors(output);
    }

    private static async Task<MetadataReference> CompileLibraryAsync(string source)
    {
        CSharpParseOptions parseOptions = new(LanguageVersion.Latest);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReferencedLibrary",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            await GetReferencesAsync(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using MemoryStream image = new();
        var emitResult = compilation.Emit(image);
        Assert.True(emitResult.Success, string.Join("\n", emitResult.Diagnostics.Select(d => d.ToString())));
        return MetadataReference.CreateFromImage(image.ToArray());
    }

    private static async Task<(Compilation Output, GeneratorDriverRunResult Result)> RunGeneratorAsync(string source, MetadataReference library)
    {
        CSharpParseOptions parseOptions = new(LanguageVersion.Latest);
        ImmutableArray<MetadataReference> references = (await GetReferencesAsync()).Add(library);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "FormatterConsumer",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
        return (output, driver.GetRunResult());
    }

    private static async Task<ImmutableArray<MetadataReference>> GetReferencesAsync()
    {
        ImmutableArray<MetadataReference> frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        return frameworkReferences.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
    }

    private static string GetFormatterSource(GeneratorDriverRunResult result)
    {
        return result.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => s.SourceText.ToString())
            .Single(s => s.Contains("partial class MyFormatter"));
    }

    private static void AssertNoErrors(Compilation compilation)
    {
        Diagnostic[] errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join("\n", errors.Select(d => d.ToString())));
    }
}
