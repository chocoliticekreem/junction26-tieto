// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageNetStandardReceipts(ITestOutputHelper logger)
{
    private const string Source = """
        using MessagePack;

        namespace TestNs
        {
            [GeneratedMessagePackResolver]
            partial class MyResolver { }

            [MessagePackObject]
            public class MyObject
            {
                [Key(0)]
                public int Value { get; set; }
            }
        }
        """;

    [Fact]
    public async Task FormattersAreDecoratedWhenNetStandard20SymbolDefined()
    {
        // 1. The attribute exists and is valid on a class when targeting netstandard2.0.
        ImmutableArray<MetadataReference> nsRefs = await ReferenceAssemblies.NetStandard.NetStandard20.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var probeTree = CSharpSyntaxTree.ParseText("[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] internal sealed class Probe { }");
        var probe = CSharpCompilation.Create("Probe", new[] { probeTree }, nsRefs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var probeErrors = probe.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(probeErrors);

        // 2. Generate code for a consumer that defines NETSTANDARD2_0.
        ImmutableArray<MetadataReference> refs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3, preprocessorSymbols: new[] { "NETSTANDARD2_0" });
        var tree = CSharpSyntaxTree.ParseText(Source, parseOptions);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { tree },
            refs.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> _);

        var generatedTrees = driver.GetRunResult().GeneratedTrees;
        var classes = generatedTrees
            .SelectMany(t => t.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Where(c => c.Identifier.ValueText.EndsWith("Formatter", StringComparison.Ordinal) || c.Identifier.ValueText == "MyResolver")
            .ToList();

        foreach (var t in generatedTrees)
        {
            logger.WriteLine(t.ToString());
        }

        Assert.NotEmpty(classes);
        var undecorated = classes
            .Where(c => !c.AttributeLists.SelectMany(a => a.Attributes).Any(a => a.Name.ToString().Contains("ExcludeFromCodeCoverage")))
            .Select(c => c.Identifier.ValueText)
            .ToList();
        Assert.True(undecorated.Count == 0, "Generated classes lacking [ExcludeFromCodeCoverage] under NETSTANDARD2_0: " + string.Join(", ", undecorated));
    }
}
