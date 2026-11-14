// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageGeneratedCodeTests(ITestOutputHelper logger)
{
    private const string AttributeName = "System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute";

    private const string Source = """
        using MessagePack;

        namespace TestNs
        {
            [GeneratedMessagePackResolver]
            partial class MyResolver
            {
            }

            [MessagePackObject]
            public class IntKeyed
            {
                [Key(0)]
                public int A { get; set; }

                [Key(1)]
                public MyEnum E { get; set; }

                [Key(2)]
                public IUnion U { get; set; }
            }

            [MessagePackObject(true)]
            public class StringKeyed
            {
                public int B { get; set; }
            }

            public enum MyEnum
            {
                X,
                Y,
            }

            [Union(0, typeof(UnionA))]
            public interface IUnion
            {
            }

            [MessagePackObject]
            public class UnionA : IUnion
            {
                [Key(0)]
                public int C { get; set; }
            }

            public partial class Outer
            {
                [MessagePackObject]
                public class Nested
                {
                    [Key(0)]
                    public int D { get; set; }
                }
            }
        }
        """;

    [Fact]
    public async Task AllGeneratedTypesAreExcludedFromCodeCoverage()
    {
        (Compilation output, ImmutableArray<SyntaxTree> generatedTrees) = await this.RunGeneratorAsync(new CSharpParseOptions(LanguageVersion.CSharp12));
        List<string> offenders = FindUndecoratedGeneratedTypes(output, generatedTrees);
        foreach (string offender in offenders)
        {
            logger.WriteLine("Not excluded from coverage: " + offender);
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public async Task GeneratedResolverIsExcludedFromCodeCoverage()
    {
        (Compilation output, _) = await this.RunGeneratorAsync(new CSharpParseOptions(LanguageVersion.CSharp12));
        INamedTypeSymbol? resolver = output.GetTypeByMetadataName("TestNs.MyResolver");
        Assert.NotNull(resolver);
        Assert.Contains(resolver!.GetAttributes(), a => a.AttributeClass?.ToDisplayString() == AttributeName);
    }

    private async Task<(Compilation Output, ImmutableArray<SyntaxTree> GeneratedTrees)> RunGeneratorAsync(CSharpParseOptions parseOptions)
    {
        ImmutableArray<MetadataReference> frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = new List<MetadataReference>(frameworkRefs);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsCoverageAssembly",
            new[] { CSharpSyntaxTree.ParseText(Source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> _);

        GeneratorDriverRunResult run = driver.GetRunResult();
        Assert.All(run.Results, r => Assert.Null(r.Exception));

        ImmutableArray<SyntaxTree> generatedTrees = output.SyntaxTrees.Where(t => !compilation.SyntaxTrees.Contains(t)).ToImmutableArray();
        Assert.NotEmpty(generatedTrees);

        List<Diagnostic> errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        foreach (Diagnostic error in errors)
        {
            logger.WriteLine(error.ToString());
        }

        Assert.Empty(errors);
        return (output, generatedTrees);
    }

    private static List<string> FindUndecoratedGeneratedTypes(Compilation output, ImmutableArray<SyntaxTree> generatedTrees)
    {
        var generatedSet = new HashSet<SyntaxTree>(generatedTrees);
        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var offenders = new List<string>();
        foreach (SyntaxTree tree in generatedTrees)
        {
            SemanticModel model = output.GetSemanticModel(tree);
            foreach (BaseTypeDeclarationSyntax decl in tree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(decl) is not INamedTypeSymbol type || !seen.Add(type))
                {
                    continue;
                }

                bool fullyGenerated = type.DeclaringSyntaxReferences.All(r => generatedSet.Contains(r.SyntaxTree));
                if (fullyGenerated && !IsExcluded(type))
                {
                    offenders.Add(type.ToDisplayString());
                }
            }
        }

        return offenders;
    }

    private static bool IsExcluded(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (current.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == AttributeName))
            {
                return true;
            }
        }

        return false;
    }
}
