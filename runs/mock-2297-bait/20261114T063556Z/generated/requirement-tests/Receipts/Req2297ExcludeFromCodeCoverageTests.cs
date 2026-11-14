// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class Req2297ExcludeFromCodeCoverageTests(ITestOutputHelper logger)
{
    private const string Source = @"
using MessagePack;

namespace Demo
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

    public enum Color { Red, Green }

    [MessagePackObject]
    public class HasEnum
    {
        [Key(0)]
        public Color C { get; set; }
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

    [GeneratedMessagePackResolver]
    partial class MyResolver
    {
    }
}
";

    [Fact]
    public async Task GeneratedFormattersAreExcludedFromCodeCoverage()
    {
        (Compilation output, List<SyntaxTree> generated) = await this.RunGeneratorAsync();
        INamedTypeSymbol attribute = GetAttributeType(output);
        List<INamedTypeSymbol> formatters = FindGeneratedFormatters(output, generated);
        Assert.NotEmpty(formatters);
        List<string> missing = formatters.Where(f => !HasAttribute(f, attribute)).Select(f => f.ToDisplayString()).ToList();
        Assert.True(missing.Count == 0, "Generated formatters without [ExcludeFromCodeCoverage]: " + string.Join(", ", missing));
        List<Diagnostic> errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    [Fact]
    public async Task GeneratedResolverIsExcludedFromCodeCoverage()
    {
        (Compilation output, List<SyntaxTree> generated) = await this.RunGeneratorAsync();
        INamedTypeSymbol attribute = GetAttributeType(output);
        INamedTypeSymbol? resolver = output.GetTypeByMetadataName("Demo.MyResolver");
        Assert.NotNull(resolver);
        Assert.True(HasAttribute(resolver!, attribute), "The generated resolver is not decorated with [ExcludeFromCodeCoverage].");
    }

    private static INamedTypeSymbol GetAttributeType(Compilation output)
    {
        INamedTypeSymbol? attribute = output.GetTypeByMetadataName("System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute");
        Assert.NotNull(attribute);
        return attribute!;
    }

    private static bool HasAttribute(INamedTypeSymbol type, INamedTypeSymbol attribute)
    {
        return type.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attribute));
    }

    private static List<INamedTypeSymbol> FindGeneratedFormatters(Compilation output, List<SyntaxTree> generated)
    {
        List<INamedTypeSymbol> result = new();
        foreach (SyntaxTree tree in generated)
        {
            SemanticModel model = output.GetSemanticModel(tree);
            foreach (ClassDeclarationSyntax declaration in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (declaration.Identifier.ValueText.EndsWith("Formatter", StringComparison.Ordinal) && model.GetDeclaredSymbol(declaration) is INamedTypeSymbol symbol)
                {
                    result.Add(symbol);
                }
            }
        }

        return result;
    }

    private async Task<(Compilation Output, List<SyntaxTree> Generated)> RunGeneratorAsync(params string[] preprocessorSymbols)
    {
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: preprocessorSymbols);
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        SyntaxTree sourceTree = CSharpSyntaxTree.ParseText(Source, parseOptions, path: "Source.cs");
        CSharpCompilation compilation = CSharpCompilation.Create("Req2297Test", new[] { sourceTree }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ISourceGenerator[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() }, parseOptions: parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        foreach (Diagnostic diagnostic in generatorDiagnostics)
        {
            logger.WriteLine(diagnostic.ToString());
        }

        List<SyntaxTree> generated = output.SyntaxTrees.Where(t => t != sourceTree).ToList();
        foreach (SyntaxTree tree in generated)
        {
            logger.WriteLine(tree.FilePath);
            logger.WriteLine(tree.ToString());
        }

        return (output, generated);
    }
}
