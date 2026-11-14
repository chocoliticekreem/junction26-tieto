// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class DuplicateKeyNotSilentlyDroppedTests
{
    private readonly ITestOutputHelper logger;

    public DuplicateKeyNotSilentlyDroppedTests(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    [Fact]
    public async Task DuplicateIntKey_IsReportedOrBothMembersSerialized()
    {
        string source = """
            using MessagePack;

            namespace TestNs
            {
                [MessagePackObject]
                public class Dup
                {
                    [Key(0)]
                    public int FirstValue { get; set; }

                    [Key(0)]
                    public int SecondValue { get; set; }
                }

                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }
            }
            """;

        var framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3);
        var compilation = CSharpCompilation.Create(
            "ReceiptsDupKey",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGenerators(compilation);
        var result = driver.GetRunResult();

        bool generatorFailed = result.Results.Any(r => r.Exception is not null);
        bool reported = result.Diagnostics.Any(d => d.Severity >= DiagnosticSeverity.Warning);
        string generated = string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));
        this.logger.WriteLine(generated);

        Assert.True(
            generatorFailed || reported || generated.Contains("SecondValue"),
            "Duplicate [Key(0)] was silently accepted: SecondValue is absent from the generated formatter and no diagnostic or failure was produced.");
    }
}
