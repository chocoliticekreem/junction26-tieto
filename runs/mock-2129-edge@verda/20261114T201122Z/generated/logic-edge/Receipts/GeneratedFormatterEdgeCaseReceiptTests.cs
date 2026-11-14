// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MessagePack.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterEdgeCaseReceiptTests
{
    [Fact]
    public async Task MessagePackAttributesOnDataTypeAreHonored()
    {
        const string source = """
            using MessagePack;
            using MessagePack.Formatters;

            [MessagePackObject]
            public class AttributedPoco
            {
                [Key(0)]
                public int Value { get; set; }

                [IgnoreMember]
                public int Ignored { get; set; }
            }

            [GeneratedMessagePackFormatter]
            public partial class AttributedExternalFormatter : IMessagePackFormatter<AttributedPoco>
            {
            }
            """;

        (GeneratorDriverRunResult result, Compilation output) = await RunGeneratorAsync(source);
        output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        string[] formatterSources = result.Results
            .SelectMany(r => r.GeneratedSources)
            .Select(s => s.SourceText.ToString())
            .Where(s => s.Contains("partial class AttributedExternalFormatter", StringComparison.Ordinal))
            .ToArray();
        formatterSources.Should().ContainSingle();
        formatterSources[0].Should().NotContain("Ignored");
    }

    [Fact]
    public async Task MultipleFormatterInterfacesAreRejected()
    {
        const string source = """
            using MessagePack;
            using MessagePack.Formatters;

            public class A
            {
                public int Value { get; set; }
            }

            public class B
            {
                public int Value { get; set; }
            }

            [GeneratedMessagePackFormatter]
            public partial class DuoFormatter : IMessagePackFormatter<A>, IMessagePackFormatter<B>
            {
            }
            """;

        (GeneratorDriverRunResult result, _) = await RunGeneratorAsync(source);
        result.Diagnostics.Should().Contain(d => d.Id == "MsgPack020");
    }

    [Fact]
    public async Task NestedFormatterRequiresPartialContainingTypes()
    {
        const string source = """
            using MessagePack;
            using MessagePack.Formatters;

            public class Poco
            {
                public int Value { get; set; }
            }

            public class Outer
            {
                [GeneratedMessagePackFormatter]
                public partial class NestedFormatter : IMessagePackFormatter<Poco>
                {
                }
            }
            """;

        (GeneratorDriverRunResult result, _) = await RunGeneratorAsync(source);
        result.Diagnostics.Should().Contain(d => d.Id == "MsgPack019");
    }

    private static async Task<(GeneratorDriverRunResult Result, Compilation Output)> RunGeneratorAsync(string source)
    {
        ImmutableArray<MetadataReference> references = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        references = references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "Receipts.GeneratedFormatter",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePackGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
        return (driver.GetRunResult(), output);
    }
}
