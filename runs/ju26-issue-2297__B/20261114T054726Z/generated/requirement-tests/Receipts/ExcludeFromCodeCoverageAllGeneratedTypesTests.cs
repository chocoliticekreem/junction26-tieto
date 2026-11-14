// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageAllGeneratedTypesTests(ITestOutputHelper logger)
{
    private const string ExcludeAttributeName = "System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute";

    [Fact]
    public async Task IntKeyFormatterAndResolver_AreExcludedFromCodeCoverage_ByDefault()
    {
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
        await this.AssertAllGeneratedTypesExcludedAsync(source);
    }

    [Fact]
    public async Task StringKeyEnumAndUnionFormatters_AreExcludedFromCodeCoverage_ByDefault()
    {
        string source = """
            using MessagePack;

            namespace TestNs
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver { }

                public enum MyEnum { One, Two }

                [Union(0, typeof(UnionA))]
                [Union(1, typeof(UnionB))]
                public interface IMyUnion { }

                [MessagePackObject]
                public class UnionA : IMyUnion
                {
                    [Key(0)]
                    public int X { get; set; }
                }

                [MessagePackObject(true)]
                public class UnionB : IMyUnion
                {
                    public string Y { get; set; }
                }

                [MessagePackObject]
                public class Holder
                {
                    [Key(0)]
                    public MyEnum EnumValue { get; set; }

                    [Key(1)]
                    public IMyUnion UnionValue { get; set; }
                }
            }
            """;
        await this.AssertAllGeneratedTypesExcludedAsync(source);
    }

    private static bool IsExcluded(INamedTypeSymbol symbol)
    {
        for (INamedTypeSymbol? current = symbol; current is not null; current = current.ContainingType)
        {
            if (current.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == ExcludeAttributeName))
            {
                return true;
            }
        }

        return false;
    }

    private async Task AssertAllGeneratedTypesExcludedAsync(string source)
    {
        var references = new List<MetadataReference>();
        references.AddRange(await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None));
        references.Add(MetadataReference.CreateFromFile(typeof(global::MessagePack.MessagePackSerializer).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(global::MessagePack.MessagePackObjectAttribute).Assembly.Location));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsCoverageAssembly",
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new global::MessagePack.SourceGenerator.MessagePackGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);

        foreach (Diagnostic d in generatorDiagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        Assert.DoesNotContain(generatorDiagnostics, d => d.Severity == DiagnosticSeverity.Error);

        List<SyntaxTree> generatedTrees = output.SyntaxTrees.Where(t => !compilation.SyntaxTrees.Contains(t)).ToList();
        Assert.NotEmpty(generatedTrees);

        var uncovered = new List<string>();
        int typeCount = 0;
        foreach (SyntaxTree tree in generatedTrees)
        {
            logger.WriteLine($"// {tree.FilePath}");
            logger.WriteLine(tree.ToString());
            SemanticModel model = output.GetSemanticModel(tree);
            foreach (TypeDeclarationSyntax decl in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                ISymbol? declared = model.GetDeclaredSymbol(decl);
                if (declared is not INamedTypeSymbol symbol)
                {
                    continue;
                }

                typeCount++;
                if (!IsExcluded(symbol))
                {
                    uncovered.Add(symbol.ToDisplayString());
                }
            }
        }

        Assert.True(typeCount > 0, "Expected the generator to emit at least one type.");
        Assert.True(uncovered.Count == 0, "Generated types not excluded from code coverage: " + string.Join(", ", uncovered));

        List<Diagnostic> errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        foreach (Diagnostic e in errors)
        {
            logger.WriteLine(e.ToString());
        }

        Assert.Empty(errors);
    }
}
