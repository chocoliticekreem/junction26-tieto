// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedResolverExcludeFromCodeCoverageTests
{
    [Fact]
    public async Task GeneratedResolver_IsExcludedFromCodeCoverage()
    {
        const string source = """
            using MessagePack;

            [MessagePackObject]
            public class MyObject
            {
                [Key(0)]
                public int Value { get; set; }
            }

            [GeneratedMessagePackResolver]
            public partial class TestResolver
            {
            }
            """;

        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var compilation = CSharpCompilation.Create(
            assemblyName: "Receipts.ExcludeFromCodeCoverage",
            syntaxTrees: new[] { syntaxTree },
            references: frameworkReferences.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { MessagePack.SourceGenerator.MessagePackGenerator .AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        var generatedSources = driver.GetRunResult().Results.Single().GeneratedSources;
        var resolver = generatedSources
            .SelectMany(generatedSource => generatedSource.SyntaxTree.GetRoot().DescendantNodes())
            .OfType<ClassDeclarationSyntax>()
            .Single(declaration => declaration.Identifier.ValueText == "TestResolver");

        bool hasExcludeFromCodeCoverage = resolver.AttributeLists
            .SelectMany(list => list.Attributes)
            .Any(attribute => attribute.Name.ToString().EndsWith("ExcludeFromCodeCoverage", StringComparison.Ordinal));

        Assert.True(hasExcludeFromCodeCoverage, "The generated resolver should be decorated with ExcludeFromCodeCoverage.");
    }
}
