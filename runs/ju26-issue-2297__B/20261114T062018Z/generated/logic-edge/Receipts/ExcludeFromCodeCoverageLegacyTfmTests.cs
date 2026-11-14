// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageLegacyTfmTests
{
    private const string AttributeName = "System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute";

    private const string Source = """
        using MessagePack;

        [MessagePackObject]
        public class MyObject
        {
            [Key(0)]
            public int A { get; set; }
        }

        [GeneratedMessagePackResolver]
        partial class MyResolver { }
        """;

    [Theory]
    [InlineData("NETSTANDARD2_0")]
    [InlineData("NETFRAMEWORK")]
    public async Task GeneratedTypesCarryAttribute_WhenTargetSymbolDefined(string symbol)
    {
        var frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = frameworkRefs.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3, preprocessorSymbols: new[] { symbol });
        Compilation compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(Source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);

        Assert.True(output.SyntaxTrees.Count() > 1, "generator produced no sources");

        INamedTypeSymbol? resolver = output.GetTypeByMetadataName("MyResolver");
        Assert.NotNull(resolver);
        Assert.True(HasAttribute(resolver!), $"Generated resolver lacks ExcludeFromCodeCoverage when {symbol} is defined");

        List<INamedTypeSymbol> formatters = AllTypes(output.Assembly.GlobalNamespace)
            .Where(t => t.AllInterfaces.Any(i => i.OriginalDefinition.Name == "IMessagePackFormatter"))
            .ToList();
        Assert.NotEmpty(formatters);
        foreach (INamedTypeSymbol formatter in formatters)
        {
            Assert.True(HasAttribute(formatter), $"Generated formatter {formatter.ToDisplayString()} lacks ExcludeFromCodeCoverage when {symbol} is defined");
        }
    }

    private static bool HasAttribute(INamedTypeSymbol type)
        => type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == AttributeName);

    private static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceOrTypeSymbol container)
    {
        foreach (ISymbol member in container.GetMembers())
        {
            if (member is INamespaceSymbol ns)
            {
                foreach (INamedTypeSymbol t in AllTypes(ns))
                {
                    yield return t;
                }
            }
            else if (member is INamedTypeSymbol type)
            {
                yield return type;
                foreach (INamedTypeSymbol nested in AllTypes(type))
                {
                    yield return nested;
                }
            }
        }
    }
}
