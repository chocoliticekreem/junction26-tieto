// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageFrameworkSymbolTests
{
    [Theory]
    [InlineData("NETFRAMEWORK")]
    [InlineData("NETSTANDARD2_0")]
    public async Task FormatterHasExcludeFromCodeCoverage(string preprocessorSymbol)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: new[] { preprocessorSymbol });
        string source = """
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

        var frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = frameworkRefs.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);

        var formatters = output
            .GetSymbolsWithName(n => n.StartsWith("MyObject", StringComparison.Ordinal) && n.EndsWith("Formatter", StringComparison.Ordinal), SymbolFilter.Type)
            .OfType<INamedTypeSymbol>()
            .ToList();

        Assert.NotEmpty(formatters);
        foreach (INamedTypeSymbol formatter in formatters)
        {
            Assert.Contains(formatter.GetAttributes(), a => a.AttributeClass?.Name == "ExcludeFromCodeCoverageAttribute");
        }
    }
}
