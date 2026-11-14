// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageNetFrameworkRequirementsTests
{
    private const string Source = """
        using MessagePack;

        [MessagePackObject]
        public partial class MyObject
        {
            [Key(0)]
            public int Value { get; set; }
        }

        public enum MyEnum
        {
            None,
            One,
        }

        [Union(0, typeof(MyDerived))]
        [MessagePackObject]
        public abstract class MyBase
        {
        }

        [MessagePackObject]
        public class MyDerived : MyBase
        {
            [Key(0)]
            public int Value { get; set; }
        }

        [GeneratedMessagePackResolver]
        public partial class CustomResolver
        {
        }
        """;

    [Fact]
    public async Task GeneratedTypesAreDecoratedOnNetFramework()
    {
        (Compilation outputCompilation, GeneratorDriverRunResult runResult) = await GenerateAsync();

        runResult.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        outputCompilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        TypeDeclarationSyntax[] generatedTypes = runResult.Results
            .SelectMany(result => result.GeneratedSources)
            .SelectMany(source => source.SyntaxTree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            .ToArray();

        generatedTypes.Should().NotBeEmpty();
        generatedTypes.Where(type => !HasExcludeFromCodeCoverage(type)).Should().BeEmpty(
            ".NET Framework 4.7.2 provides ExcludeFromCodeCoverageAttribute, so generated types should retain the attribute on this target");
    }

    private static bool HasExcludeFromCodeCoverage(TypeDeclarationSyntax type)
    {
        return type.AttributeLists
            .SelectMany(list => list.Attributes)
            .Any(attribute => attribute.Name.ToString() == "global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage");
    }

    private static async Task<(Compilation OutputCompilation, GeneratorDriverRunResult RunResult)> GenerateAsync()
    {
        CSharpParseOptions parseOptions = new(
            LanguageVersion.CSharp7_3,
            preprocessorSymbols: new[] { "NETFRAMEWORK", "NET472" });
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(Source, parseOptions);
        ImmutableArray<MetadataReference> references = await ReferenceAssemblies.NetFramework.Net472.Default.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        references = references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: "ExcludeFromCodeCoverageNetFrameworkReceipts",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        ISourceGenerator generator = new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { generator }, parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out _);
        return (outputCompilation, driver.GetRunResult());
    }
}
