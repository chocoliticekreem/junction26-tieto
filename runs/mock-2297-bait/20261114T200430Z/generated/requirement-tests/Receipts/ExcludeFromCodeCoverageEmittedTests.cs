// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageEmittedTests(ITestOutputHelper logger)
{
    private const string AttributeName = "System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute";

    private const string Source = """
        using MessagePack;

        namespace MyNs
        {
            [GeneratedMessagePackResolver]
            partial class MyResolver { }

            [MessagePackObject]
            public class IntKeyed
            {
                [Key(0)] public int A { get; set; }
                [Key(1)] public MyEnum E { get; set; }
            }

            [MessagePackObject]
            public class StringKeyed
            {
                [Key("a")] public int A { get; set; }
            }

            public enum MyEnum { One, Two }

            [Union(0, typeof(UnionCase))]
            public interface IMyUnion { }

            [MessagePackObject]
            public class UnionCase : IMyUnion
            {
                [Key(0)] public int X { get; set; }
            }
        }
        """;

    [Fact]
    public async Task AllGeneratedTypesAreExcludedFromCodeCoverage()
    {
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3);
        ImmutableArray<MetadataReference> frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        IEnumerable<MetadataReference> allRefs = Enumerable.Concat<MetadataReference>(frameworkRefs, ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsCoverageAssembly",
            new[] { CSharpSyntaxTree.ParseText(Source, parseOptions, path: "Source.cs") },
            allRefs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);

        foreach (Diagnostic d in generatorDiagnostics)
        {
            logger.WriteLine("generator: " + d);
        }

        List<SyntaxTree> generatedTrees = output.SyntaxTrees.Where(t => !compilation.SyntaxTrees.Contains(t)).ToList();
        Assert.NotEmpty(generatedTrees);

        List<Diagnostic> errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        foreach (Diagnostic e in errors)
        {
            logger.WriteLine("error: " + e);
        }

        Assert.Empty(errors);

        HashSet<string> checkedTypes = new HashSet<string>();
        HashSet<string> missing = new HashSet<string>();
        foreach (SyntaxTree tree in generatedTrees)
        {
            SemanticModel model = output.GetSemanticModel(tree);
            foreach (BaseTypeDeclarationSyntax decl in tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(decl) is not INamedTypeSymbol symbol)
                {
                    continue;
                }

                bool fullyGenerated = symbol.DeclaringSyntaxReferences.All(r => generatedTrees.Contains(r.SyntaxTree));
                bool isResolver = symbol.Name == "MyResolver";
                if (!fullyGenerated && !isResolver)
                {
                    continue;
                }

                string name = symbol.ToDisplayString();
                checkedTypes.Add(name);
                if (!symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == AttributeName))
                {
                    missing.Add(name);
                }
            }
        }

        foreach (string t in checkedTypes)
        {
            logger.WriteLine("checked: " + t);
        }

        Assert.Contains("MyNs.MyResolver", checkedTypes);
        Assert.True(checkedTypes.Count >= 2, "expected generated formatter types besides the resolver");
        Assert.True(missing.Count == 0, "generated types without ExcludeFromCodeCoverage: " + string.Join(", ", missing));
    }
}
