// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class DuplicateKeyMemberReceiptTests
{
    private static async Task<(GeneratorRunResult Result, ImmutableArray<Diagnostic> Diagnostics)> RunGeneratorAsync(string source)
    {
        var parseOptions = new CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp7_3);
        var frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = frameworkRefs.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        var compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator());
        driver = driver.WithUpdatedParseOptions(parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
        GeneratorRunResult result = driver.GetRunResult().Results.Single();
        return (result, diagnostics);
    }

    [Fact]
    public async Task DuplicateIntKeyOnFields_DoesNotThrow()
    {
        string source = """
            using MessagePack;

            namespace TestNs
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver { }

                [MessagePackObject]
                public class DupFields
                {
                    [Key(0)]
                    public int First;

                    [Key(0)]
                    public int Second;
                }
            }
            """;

        var (result, diagnostics) = await RunGeneratorAsync(source);

        result.Exception.Should().BeNull("duplicate int keys on fields must not crash the type collector");
        diagnostics.Where(d => d.Id == "CS8785").Should().BeEmpty();
    }

    [Fact]
    public async Task DuplicateIntKeyOnProperties_IsReportedOrBothMembersGenerated()
    {
        string source = """
            using MessagePack;

            namespace TestNs
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver { }

                [MessagePackObject]
                public class DupProps
                {
                    [Key(0)]
                    public int First { get; set; }

                    [Key(0)]
                    public int Second { get; set; }
                }
            }
            """;

        var (result, diagnostics) = await RunGeneratorAsync(source);

        bool errorReported = result.Exception is not null
            || diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)
            || result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
        bool secondGenerated = result.GeneratedSources.Any(s => s.SourceText.ToString().Contains("Second"));

        (errorReported || secondGenerated).Should().BeTrue("a member with a duplicate key must not be silently dropped from the generated formatter");
    }
}
