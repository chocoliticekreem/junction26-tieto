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
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageTemplateEdgeTests
{
    [Fact]
    public async Task NetStandard20GeneratedTypesAreDecorated()
    {
        const string source = """
            using MessagePack;

            [MessagePackObject]
            public class Sample
            {
                [Key(0)]
                public int Value { get; set; }
            }

            [GeneratedMessagePackResolver]
            public partial class SampleResolver
            {
            }
            """;

        string[] generatedSources = await GenerateAsync(source);
        var parseOptions = new CSharpParseOptions(
            LanguageVersion.CSharp7_3,
            preprocessorSymbols: new[] { "NETSTANDARD", "NETSTANDARD2_0" });
        ClassDeclarationSyntax[] generatedClasses = generatedSources
            .SelectMany(text => CSharpSyntaxTree.ParseText(text, parseOptions)
                .GetRoot()
                .DescendantNodes()
                .OfType<ClassDeclarationSyntax>())
            .ToArray();

        Assert.NotEmpty(generatedClasses);
        Assert.All(
            generatedClasses,
            type => Assert.Contains(
                type.AttributeLists.SelectMany(list => list.Attributes),
                attribute => attribute.Name.ToString().Contains("ExcludeFromCodeCoverage", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task UserAttributedPartialResolverStillCompiles()
    {
        const string source = """
            using MessagePack;

            [MessagePackObject]
            public class Sample
            {
                [Key(0)]
                public int Value { get; set; }
            }

            [GeneratedMessagePackResolver]
            [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
            public partial class SampleResolver
            {
            }
            """;

        var test = new VerifyCS.Test
        {
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
        };
        test.TestState.Sources.Add(source);
        await test.RunAsync();
    }

    private static async Task<string[]> GenerateAsync(string source)
    {
        IEnumerable<MetadataReference> references = (await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None))
            .Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptCompilation",
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult().Results
            .SelectMany(result => result.GeneratedSources)
            .Select(generatedSource => generatedSource.SourceText.ToString())
            .ToArray();
    }
}
