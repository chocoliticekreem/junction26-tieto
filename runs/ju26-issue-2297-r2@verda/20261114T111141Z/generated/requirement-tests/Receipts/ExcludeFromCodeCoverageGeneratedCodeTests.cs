// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageGeneratedCodeTests
{
    [Fact]
    public async Task AllGeneratedTypesHaveExcludeFromCodeCoverage()
    {
        const string source = """
            using MessagePack;

            [GeneratedMessagePackResolver]
            public partial class TestResolver
            {
            }

            [MessagePackObject]
            public class MapSample
            {
                [Key(0)]
                public SampleEnum Value { get; set; }
            }

            [MessagePackObject(true)]
            public class StringKeySample
            {
                public int Value { get; set; }
            }

            public enum SampleEnum
            {
                None,
                Value,
            }

            [MessagePackObject]
            [Union(0, typeof(DerivedSample))]
            public abstract class UnionSample
            {
            }

            [MessagePackObject]
            public class DerivedSample : UnionSample
            {
                [Key(0)]
                public int Value { get; set; }
            }
            """;

        ImmutableArray<MetadataReference> frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        MetadataReference[] references = frameworkReferences
            .Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack))
            .ToArray();

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: "ExcludeFromCodeCoverageReceipt",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp7_3)) },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> generatorDiagnostics);

        Diagnostic[] generatorErrors = generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(generatorErrors.Length == 0, string.Join(Environment.NewLine, generatorErrors.Select(d => d.ToString())));

        Diagnostic[] compileErrors = outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(compileErrors.Length == 0, string.Join(Environment.NewLine, compileErrors.Select(d => d.ToString())));

        GeneratorDriverRunResult runResult = driver.GetRunResult();
        int generatedTypeCount = 0;
        List<string> undecoratedTypes = new();

        foreach (SyntaxTree generatedTree in runResult.GeneratedTrees)
        {
            SemanticModel semanticModel = outputCompilation.GetSemanticModel(generatedTree);
            foreach (ClassDeclarationSyntax classDeclaration in generatedTree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                INamedTypeSymbol generatedType = semanticModel.GetDeclaredSymbol(classDeclaration)!;
                Assert.NotNull(generatedType);
                generatedTypeCount++;

                bool hasAttribute = generatedType.GetAttributes().Any(
                    attribute => attribute.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute");
                if (!hasAttribute)
                {
                    undecoratedTypes.Add($"{generatedType.ToDisplayString()} ({generatedTree.FilePath})");
                }
            }
        }

        Assert.True(generatedTypeCount > 0, "The generator did not produce any generated classes to inspect.");
        Assert.True(
            undecoratedTypes.Count == 0,
            "Generated types without ExcludeFromCodeCoverage: " + string.Join(", ", undecoratedTypes));
    }
}
