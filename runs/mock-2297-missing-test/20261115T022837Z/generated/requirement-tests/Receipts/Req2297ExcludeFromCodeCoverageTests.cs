// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class Req2297ExcludeFromCodeCoverageTests
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

    public enum MyEnum { First, Second }

    [MessagePackObject]
    public class HasEnum
    {
        [Key(0)]
        public MyEnum E { get; set; }
    }

    [Union(0, typeof(UnionA))]
    public interface IUnion
    {
    }

    [MessagePackObject]
    public class UnionA : IUnion
    {
        [Key(0)]
        public int X { get; set; }
    }

    [MessagePackObject]
    public class HasUnion
    {
        [Key(0)]
        public IUnion U { get; set; }
    }

    [GeneratedMessagePackResolver]
    partial class MyResolver
    {
    }

    [CompositeResolver(typeof(MyResolver), typeof(MessagePack.Resolvers.StandardResolver))]
    partial class MyCompositeResolver
    {
    }
}
""";

    private static async Task<(GeneratorDriverRunResult Result, Compilation Output)> RunGeneratorAsync(params string[] preprocessorSymbols)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3, preprocessorSymbols: preprocessorSymbols);
        var frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = new List<MetadataReference>(frameworkRefs);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Req2297TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(Source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator()).WithUpdatedParseOptions(parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
        return (driver.GetRunResult(), output);
    }

    private static List<string> FindUndecoratedGeneratedTypes(GeneratorDriverRunResult result)
    {
        var missing = new List<string>();
        foreach (SyntaxTree tree in result.GeneratedTrees)
        {
            foreach (TypeDeclarationSyntax type in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                // Pure nesting containers (only nested types, no members of their own) are skipped.
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

        return missing;
    }

    [Fact]
    public async Task AllGeneratedTypesAreDecoratedWithExcludeFromCodeCoverage()
    {
        var (result, _) = await RunGeneratorAsync();
        Assert.NotEmpty(result.GeneratedTrees);
        List<string> missing = FindUndecoratedGeneratedTypes(result);
        Assert.True(missing.Count == 0, "Generated types without ExcludeFromCodeCoverage: " + string.Join(", ", missing));
    }

    [Fact]
    public async Task GeneratedCodeWithAttributeCompilesWithoutErrors()
    {
        var (result, output) = await RunGeneratorAsync();
        Assert.NotEmpty(result.GeneratedTrees);
        Assert.Empty(FindUndecoratedGeneratedTypes(result));
        var generatedTrees = new HashSet<SyntaxTree>(result.GeneratedTrees);
        var errors = output.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Location.SourceTree is not null && generatedTrees.Contains(d.Location.SourceTree))
            .Select(d => d.ToString())
            .ToList();
        Assert.True(errors.Count == 0, "Errors in generated code: " + string.Join(Environment.NewLine, errors));
    }
}
