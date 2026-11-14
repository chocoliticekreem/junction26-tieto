// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class MetadataGenericClassCompatibilityTests
{
    [Fact]
    public async Task ClosedGenericClassFromReferencedAssembly_KeepsGeneratedFormatter()
    {
        const string LibrarySource = """
            using MessagePack;
            namespace Lib;

            [MessagePackObject]
            public partial class Item<T>
            {
                [Key(0)]
                public string Key { get; set; }
            }
            """;

        const string AppSource = """
            using MessagePack;
            using Lib;
            namespace App;

            [MessagePackObject]
            public sealed partial record Cmd([property: Key(0)] string Scope)
            {
                [Key(1)]
                public Item<int> Member { get; init; }
            }

            [GeneratedMessagePackResolver]
            public partial class TestResolver
            {
            }
            """;

        ImmutableArray<MetadataReference> frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        ImmutableArray<MetadataReference> references = frameworkReferences.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        CSharpParseOptions parseOptions = new(LanguageVersion.Latest);
        CSharpCompilation libraryCompilation = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(LibrarySource, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        libraryCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        CSharpCompilation appCompilation = CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource, parseOptions) },
            references.Add(libraryCompilation.ToMetadataReference()),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator(),
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            appCompilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> generatorDiagnostics);

        generatorDiagnostics.Where(d => d.Id == "CS8785").Should().BeEmpty();
        driver.GetRunResult().GeneratedTrees.Select(t => t.FilePath).Should().Contain(
            path => path.EndsWith("App.TestResolver.g.cs", StringComparison.Ordinal));

        INamedTypeSymbol itemDefinition = outputCompilation.GetTypeByMetadataName("Lib.Item`1")!;
        itemDefinition.Should().NotBeNull();
        INamedTypeSymbol formatterDefinition = outputCompilation.GetTypeByMetadataName("MessagePack.IMessagePackFormatter`1")!;
        formatterDefinition.Should().NotBeNull();
        INamedTypeSymbol closedItem = itemDefinition.Construct(outputCompilation.GetSpecialType(SpecialType.System_Int32));
        INamedTypeSymbol closedFormatterInterface = formatterDefinition.Construct(closedItem);

        HasGeneratedImplementation(outputCompilation, closedFormatterInterface).Should().BeTrue(
            "closed generic classes from referenced assemblies must retain the formatter they had before the value-type crash fix");
    }

    private static bool HasGeneratedImplementation(Compilation compilation, INamedTypeSymbol formatterInterface)
    {
        foreach (SyntaxTree tree in compilation.SyntaxTrees)
        {
            if (!tree.FilePath.EndsWith(".g.cs", StringComparison.Ordinal))
            {
                continue;
            }

            SemanticModel semanticModel = compilation.GetSemanticModel(tree);
            foreach (ClassDeclarationSyntax declaration in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                INamedTypeSymbol? symbol = semanticModel.GetDeclaredSymbol(declaration);
                if (symbol?.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, formatterInterface)) == true)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
