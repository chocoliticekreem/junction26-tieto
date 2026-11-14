// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageGeneratedTests
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

            [MessagePackObject(true)]
            public class MapObject
            {
                public string Name { get; set; }
            }

            [Union(0, typeof(UnionA))]
            public interface IUnionBase { }

            [MessagePackObject]
            public class UnionA : IUnionBase
            {
                [Key(0)]
                public int X { get; set; }
            }

            [GeneratedMessagePackResolver]
            public partial class MyResolver { }
        }
        """;

    [Fact]
    public async Task GeneratedFormatters_AreExcludedFromCodeCoverage()
    {
        Compilation output = await RunGeneratorAsync();
        List<INamedTypeSymbol> formatters = AllTypes(output).Where(IsFormatter).ToList();
        Assert.True(formatters.Count >= 4, $"Expected at least 4 generated formatters, found {formatters.Count}.");
        foreach (INamedTypeSymbol formatter in formatters)
        {
            Assert.True(HasExcludeFromCodeCoverage(formatter), $"{formatter.ToDisplayString()} is not decorated with ExcludeFromCodeCoverage.");
        }
    }

    [Fact]
    public async Task GeneratedResolver_IsExcludedFromCodeCoverage()
    {
        Compilation output = await RunGeneratorAsync();
        INamedTypeSymbol? resolver = output.GetTypeByMetadataName("Sample.MyResolver");
        Assert.NotNull(resolver);
        Assert.True(HasExcludeFromCodeCoverage(resolver!), "The generated resolver is not decorated with ExcludeFromCodeCoverage.");
    }

    private static async Task<Compilation> RunGeneratorAsync()
    {
        CSharpParseOptions parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest);
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        Compilation compilation = CSharpCompilation.Create(
            "ReceiptsCoverageAssembly",
            new[] { CSharpSyntaxTree.ParseText(Source, parseOptions) },
            framework,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddReferences(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> _);
        return output;
    }

    private static bool IsFormatter(INamedTypeSymbol type)
        => type.AllInterfaces.Any(i => i.Name == "IMessagePackFormatter" && i.ContainingNamespace.ToDisplayString() == "MessagePack.Formatters");

    private static bool HasExcludeFromCodeCoverage(INamedTypeSymbol type)
        => type.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute");

    private static IEnumerable<INamedTypeSymbol> AllTypes(Compilation compilation)
    {
        var result = new List<INamedTypeSymbol>();
        Collect(compilation.Assembly.GlobalNamespace, result);
        return result;
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
