// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageDownlevelTargetsTests
{
    private const string Source = """
        using MessagePack;

        namespace Sample
        {
            public enum MyEnum { A, B }

            [MessagePackObject]
            public class MyObject
            {
                [Key(0)]
                public int Value { get; set; }

                [Key(1)]
                public MyEnum Kind { get; set; }
            }

            [GeneratedMessagePackResolver]
            public partial class MyResolver { }
        }
        """;

    [Theory]
    [InlineData("NETSTANDARD2_0")]
    [InlineData("NETFRAMEWORK")]
    public async Task GeneratedTypes_AreExcludedFromCodeCoverage_OnDownlevelTargets(string targetSymbol)
    {
        CSharpParseOptions parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Latest)
            .WithPreprocessorSymbols(targetSymbol);
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        Compilation compilation = CSharpCompilation.Create(
            "ReceiptsDownlevelAssembly",
            new[] { CSharpSyntaxTree.ParseText(Source, parseOptions) },
            framework,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddReferences(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        Assert.NotNull(compilation.GetTypeByMetadataName("System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute"));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> _);

        var all = new List<INamedTypeSymbol>();
        Collect(output.Assembly.GlobalNamespace, all);
        List<INamedTypeSymbol> formatters = all.Where(t => t.AllInterfaces.Any(i => i.Name == "IMessagePackFormatter" && i.ContainingNamespace.ToDisplayString() == "MessagePack.Formatters")).ToList();
        Assert.True(formatters.Count >= 2, $"Expected at least 2 generated formatters, found {formatters.Count}.");

        INamedTypeSymbol? resolver = output.GetTypeByMetadataName("Sample.MyResolver");
        Assert.NotNull(resolver);

        foreach (INamedTypeSymbol type in formatters.Append(resolver!))
        {
            Assert.True(
                type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute"),
                $"{type.ToDisplayString()} is not decorated with ExcludeFromCodeCoverage when {targetSymbol} is defined.");
        }
    }

    private static void Collect(INamespaceOrTypeSymbol container, List<INamedTypeSymbol> result)
    {
        foreach (ISymbol member in container.GetMembers())
        {
            if (member is INamespaceSymbol ns)
            {
                Collect(ns, result);
            }
            else if (member is INamedTypeSymbol type)
            {
                result.Add(type);
                Collect(type, result);
            }
        }
    }
}
