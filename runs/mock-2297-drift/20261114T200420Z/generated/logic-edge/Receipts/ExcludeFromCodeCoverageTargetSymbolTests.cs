// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageTargetSymbolTests
{
    private const string Source = """
        using MessagePack;

        namespace MyNs
        {
            [GeneratedMessagePackResolver]
            partial class MyResolver { }

            public enum MyEnum { A, B }

            [MessagePackObject]
            public class MyObject
            {
                [Key(0)]
                public int Value { get; set; }

                [Key(1)]
                public MyEnum E { get; set; }
            }
        }
        """;

    [Theory]
    [InlineData("NETSTANDARD2_0")]
    [InlineData("NETFRAMEWORK")]
    public async Task GeneratedFormatters_HaveExcludeFromCodeCoverage_WhenTargeting(string symbol)
    {
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.CSharp7_3).WithPreprocessorSymbols(symbol);
        ImmutableArray<MetadataReference> frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        ImmutableArray<MetadataReference> refs = frameworkRefs
            .Add(MetadataReference.CreateFromFile(typeof(MessagePack.MessagePackObjectAttribute).Assembly.Location))
            .Add(MetadataReference.CreateFromFile(typeof(MessagePack.MessagePackSerializer).Assembly.Location));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(Source, parseOptions) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGenerators(compilation);
        GeneratorDriverRunResult result = driver.GetRunResult();

        var formatterClasses = result.GeneratedTrees
            .SelectMany(t => t.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Where(c => c.Identifier.ValueText.EndsWith("Formatter", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(formatterClasses);
        foreach (ClassDeclarationSyntax c in formatterClasses)
        {
            Assert.True(
                c.AttributeLists.SelectMany(l => l.Attributes).Any(a => a.Name.ToString().Contains("ExcludeFromCodeCoverage")),
                $"Generated class {c.Identifier.ValueText} lacks ExcludeFromCodeCoverage when {symbol} is defined, although the attribute exists on that target.");
        }
    }
}
