// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class StringKeyMemberLoopTests
{
    private readonly ITestOutputHelper logger;

    public StringKeyMemberLoopTests(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    [Fact]
    public async Task StringKeyedFieldAndPropertyAreBothInGeneratedFormatter()
    {
        string source = """
            using MessagePack;

            namespace ReceiptsNs
            {
                [MessagePackObject]
                public class MyObject
                {
                    [Key("f")]
                    public int FieldWithStringKeyZzq;

                    [Key("p")]
                    public int PropertyWithStringKeyZzq { get; set; }
                }

                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }
            }
            """;

        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3);
        var frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = frameworkRefs.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        var compilation = CSharpCompilation.Create(
            "ReceiptsStringKeyAsm",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGenerators(compilation);
        var result = driver.GetRunResult();

        foreach (var d in result.Diagnostics)
        {
            this.logger.WriteLine(d.ToString());
        }

        string allGenerated = string.Join("\n", result.GeneratedTrees.Select(t => t.GetText().ToString()));
        this.logger.WriteLine(allGenerated);

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("FieldWithStringKeyZzq", allGenerated);
        Assert.Contains("PropertyWithStringKeyZzq", allGenerated);
    }
}
