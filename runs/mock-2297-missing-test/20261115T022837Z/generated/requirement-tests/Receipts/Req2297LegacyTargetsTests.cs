// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class Req2297LegacyTargetsTests
{
    private const string Source = """
using MessagePack;

namespace MyNs
{
    [MessagePackObject]
    public class IntKeyed
    {
        [Key(0)]
        public int A { get; set; }
    }

    [MessagePackObject(true)]
    public class StringKeyed
    {
        public int B { get; set; }
    }

    [GeneratedMessagePackResolver]
    partial class MyResolver
    {
    }
}
""";

    // ExcludeFromCodeCoverageAttribute exists in netstandard2.0 and .NET Framework 4.0+,
    // so "all generated code" should carry it for consumers on those targets too.
    [Theory]
    [InlineData("NETSTANDARD2_0")]
    [InlineData("NETFRAMEWORK")]
    public async Task GeneratedTypesAreDecoratedWhenConsumerTargetsLegacyFramework(string symbol)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3, preprocessorSymbols: new[] { symbol });
        var frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = new List<MetadataReference>(frameworkRefs);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Req2297LegacyAssembly",
            new[] { CSharpSyntaxTree.ParseText(Source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator()).WithUpdatedParseOptions(parseOptions);
        driver = driver.RunGenerators(compilation);
        GeneratorDriverRunResult result = driver.GetRunResult();
        Assert.NotEmpty(result.GeneratedTrees);

        var missing = new List<string>();
        foreach (SyntaxTree tree in result.GeneratedTrees)
        {
            foreach (TypeDeclarationSyntax type in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (!type.Members.Any(m => m is not BaseTypeDeclarationSyntax))
                {
                    continue;
                }

                bool decorated = type.AttributeLists
                    .SelectMany(l => l.Attributes)
                    .Any(a => a.Name.ToString().Contains("ExcludeFromCodeCoverage", StringComparison.Ordinal));
                if (!decorated)
                {
                    missing.Add($"{Path.GetFileName(tree.FilePath)}: {type.Identifier.ValueText}");
                }
            }
        }

        Assert.True(missing.Count == 0, $"With {symbol} defined, generated types without an active ExcludeFromCodeCoverage: " + string.Join(", ", missing));
    }
}
