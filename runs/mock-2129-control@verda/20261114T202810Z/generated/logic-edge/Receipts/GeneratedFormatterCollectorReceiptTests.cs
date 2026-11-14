// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterCollectorReceiptTests
{
    [Fact]
    public async Task MessagePackObjectKeysOnReferencedTypeAreHonored()
    {
        ImmutableArray<MetadataReference> references = await GetReferencesAsync();
        CSharpCompilation referencedCompilation = CreateCompilation(
            "ReferencedLibrary",
            """
            using MessagePack;

            [MessagePackObject]
            public class ReferencedType
            {
                [Key(0)]
                public int Value { get; set; }
            }
            """,
            references);

        using MemoryStream image = new();
        referencedCompilation.Emit(image).Success.Should().BeTrue();
        MetadataReference referencedLibrary = MetadataReference.CreateFromImage(image.ToArray());

        CSharpCompilation compilation = CreateCompilation(
            "Consumer",
            """
            using MessagePack;
            using MessagePack.Formatters;

            [GeneratedMessagePackFormatter]
            public partial class MyFormatter : IMessagePackFormatter<ReferencedType>;
            """,
            references.Add(referencedLibrary));

        GeneratorDriver driver = RunGenerator(compilation, out _);
        string formatter = driver.GetRunResult().Results.Single().GeneratedSources
            .Select(source => source.SourceText.ToString())
            .Single(text => text.Contains("partial class MyFormatter", StringComparison.Ordinal));

        formatter.Should().Contain("WriteArrayHeader");
        formatter.Should().NotContain("WriteMapHeader");
    }

    [Fact]
    public async Task BaseTypeExplicitCallbacks_Compile()
    {
        ImmutableArray<MetadataReference> references = await GetReferencesAsync();
        CSharpCompilation compilation = CreateCompilation(
            "Consumer",
            """
            using MessagePack;
            using MessagePack.Formatters;

            public class Base : IMessagePackSerializationCallbackReceiver
            {
                void IMessagePackSerializationCallbackReceiver.OnBeforeSerialize()
                {
                }

                void IMessagePackSerializationCallbackReceiver.OnAfterDeserialize()
                {
                }
            }

            public class Derived : Base
            {
                public int Value { get; set; }
            }

            [GeneratedMessagePackFormatter]
            public partial class DerivedFormatter : IMessagePackFormatter<Derived>;
            """,
            references);

        RunGenerator(compilation, out Compilation outputCompilation);
        outputCompilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty();
    }

    private static async Task<ImmutableArray<MetadataReference>> GetReferencesAsync()
    {
        ImmutableArray<MetadataReference> frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(
            LanguageNames.CSharp,
            CancellationToken.None);
        return frameworkReferences.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
    }

    private static CSharpCompilation CreateCompilation(
        string assemblyName,
        string source,
        IEnumerable<MetadataReference> references)
    {
        return CSharpCompilation.Create(
            assemblyName,
            new[]
            {
                CSharpSyntaxTree.ParseText(
                    source,
                    new CSharpParseOptions(LanguageVersion.CSharp12)),
            },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static GeneratorDriver RunGenerator(CSharpCompilation compilation, out Compilation outputCompilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator());
        return driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out outputCompilation,
            out ImmutableArray<Diagnostic> _);
    }
}
