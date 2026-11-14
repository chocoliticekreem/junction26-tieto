// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageGenerationTests
{
    [Fact]
    public async Task GeneratedClassesAreDecoratedAndCompile()
    {
        const string Source = """
            using MessagePack;

            [GeneratedMessagePackResolver]
            public partial class CoverageResolver
            {
            }

            [MessagePackObject]
            public class CoveragePoco
            {
                [Key(0)]
                public int Value { get; set; }
            }
            """;

        CSharpParseOptions parseOptions = new(LanguageVersion.CSharp7_3);
        IEnumerable<MetadataReference> frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        MetadataReference[] references = frameworkReferences
            .Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack))
            .ToArray();

        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName: "ExcludeFromCodeCoverageReceipt",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(Source, parseOptions) },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new IIncrementalGenerator[] { new MessagePack.SourceGenerator.MessagePackGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out var generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Empty(outputCompilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        SyntaxTree[] generatedTrees = driver.GetRunResult().Results
            .SelectMany(result => result.GeneratedSources)
            .Select(source => source.SyntaxTree)
            .ToArray();
        Assert.NotEmpty(generatedTrees);

        foreach (SyntaxTree tree in generatedTrees)
        {
            SyntaxNode root = await tree.GetRootAsync();
            ClassDeclarationSyntax[] generatedClasses = root.DescendantNodes().OfType<ClassDeclarationSyntax>().ToArray();
            Assert.NotEmpty(generatedClasses);

            foreach (ClassDeclarationSyntax declaration in generatedClasses)
            {
                bool decorated = declaration.AttributeLists
                    .SelectMany(list => list.Attributes)
                    .Any(attribute => attribute.Name.ToString().EndsWith("ExcludeFromCodeCoverage", StringComparison.Ordinal));
                Assert.True(decorated, $"{declaration.Identifier.Text} in {tree.FilePath} lacks ExcludeFromCodeCoverage.");
            }
        }
    }
}
