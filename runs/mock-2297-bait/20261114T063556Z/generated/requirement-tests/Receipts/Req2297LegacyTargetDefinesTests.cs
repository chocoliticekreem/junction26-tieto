// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class Req2297LegacyTargetDefinesTests(ITestOutputHelper logger)
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

    [GeneratedMessagePackResolver]
    partial class MyResolver
    {
    }
}
";

    [Theory]
    [InlineData("NETSTANDARD2_0")]
    [InlineData("NETFRAMEWORK")]
    public async Task GeneratedFormattersAreExcludedFromCodeCoverageUnderLegacyTargetDefines(string targetDefine)
    {
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: new[] { targetDefine });
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        SyntaxTree sourceTree = CSharpSyntaxTree.ParseText(Source, parseOptions, path: "Source.cs");
        CSharpCompilation compilation = CSharpCompilation.Create("Req2297LegacyTest", new[] { sourceTree }, references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ISourceGenerator[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() }, parseOptions: parseOptions);
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        foreach (Diagnostic diagnostic in generatorDiagnostics)
        {
            logger.WriteLine(diagnostic.ToString());
        }

        INamedTypeSymbol? attribute = output.GetTypeByMetadataName("System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverageAttribute");
        Assert.NotNull(attribute);

        List<INamedTypeSymbol> types = new();
        foreach (SyntaxTree tree in output.SyntaxTrees.Where(t => t != sourceTree))
        {
            SemanticModel model = output.GetSemanticModel(tree);
            foreach (ClassDeclarationSyntax declaration in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (declaration.Identifier.ValueText.EndsWith("Formatter", StringComparison.Ordinal) && model.GetDeclaredSymbol(declaration) is INamedTypeSymbol symbol)
                {
                    types.Add(symbol);
                }
            }
        }

        Assert.NotEmpty(types);
        INamedTypeSymbol? resolver = output.GetTypeByMetadataName("Demo.MyResolver");
        Assert.NotNull(resolver);
        types.Add(resolver!);

        List<string> missing = types
            .Where(t => !t.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attribute)))
            .Select(t => t.ToDisplayString())
            .ToList();
        Assert.True(missing.Count == 0, $"With {targetDefine} defined these generated types lack [ExcludeFromCodeCoverage]: " + string.Join(", ", missing));
    }
}
