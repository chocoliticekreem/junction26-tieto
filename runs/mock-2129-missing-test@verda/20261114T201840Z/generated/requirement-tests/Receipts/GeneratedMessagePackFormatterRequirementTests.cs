// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedMessagePackFormatterRequirementTests
{
    private const string FormatterSource = """
        using MessagePack;
        using MessagePack.Formatters;

        [GeneratedMessagePackFormatter]
        public partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject>;
        """;

    [Fact]
    public async Task AttributeFreeReferencedAssemblyType_GetsBodylessFormatter()
    {
        const string projectASource = """
            namespace ProjectA;

            public class MyObject
            {
                public int Id { get; set; }

                public string Name { get; set; } = "";
            }
            """;

        MetadataReference projectA = await CompileProjectAAsync(projectASource, includeMessagePack: false);
        (GeneratorDriverRunResult result, Compilation output) = await RunGeneratorAsync(projectA);

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        string formatter = GetFormatterSource(result);
        formatter.Should().Contain("partial class MyObjectFormatter");
        formatter.Should().Contain("ProjectA.MyObject");
    }

    [Fact]
    public async Task MessagePackAttributesOnTargetType_AreHonored()
    {
        const string projectASource = """
            using MessagePack;

            namespace ProjectA;

            [MessagePackObject]
            public class MyObject
            {
                [Key(0)]
                public int Id { get; set; }

                [IgnoreMember]
                public string Ignored { get; set; } = "";
            }
            """;

        MetadataReference projectA = await CompileProjectAAsync(projectASource, includeMessagePack: true);
        (GeneratorDriverRunResult result, Compilation output) = await RunGeneratorAsync(projectA);

        result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        string formatter = GetFormatterSource(result);
        formatter.Should().Contain("WriteArrayHeader(1)");
        formatter.Should().NotContain("Ignored");
    }

    private static async Task<MetadataReference> CompileProjectAAsync(string source, bool includeMessagePack)
    {
        List<MetadataReference> references = (await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None)).ToList();
        if (includeMessagePack)
        {
            references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        }

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using MemoryStream stream = new();
        EmitResult emit = compilation.Emit(stream);
        emit.Success.Should().BeTrue(string.Join(Environment.NewLine, emit.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static async Task<(GeneratorDriverRunResult Result, Compilation Output)> RunGeneratorAsync(MetadataReference projectA)
    {
        List<MetadataReference> references = (await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None)).ToList();
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        references.Add(projectA);

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(FormatterSource, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _, CancellationToken.None);
        return (driver.GetRunResult(), output);
    }

    private static string GetFormatterSource(GeneratorDriverRunResult result)
    {
        return result.GeneratedTrees
            .Select(tree => tree.ToString())
            .Single(text => text.Contains("partial class MyObjectFormatter", StringComparison.Ordinal));
    }
}
