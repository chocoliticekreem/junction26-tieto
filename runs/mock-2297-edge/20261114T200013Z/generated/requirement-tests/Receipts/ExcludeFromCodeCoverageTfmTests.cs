// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageTfmTests
{
    private const string Source = """
        using MessagePack;

        namespace TestNs
        {
            [GeneratedMessagePackResolver]
            partial class MyResolver { }

            [MessagePackObject]
            public class MyObject
            {
                [Key(0)]
                public int Value { get; set; }
            }
        }
        """;

    [Theory]
    [InlineData("NETSTANDARD2_0")]
    [InlineData("NETFRAMEWORK")]
    public async Task GeneratedCode_IsExcludedFromCodeCoverage_OnAllTargetFrameworks(string tfmSymbol)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest).WithPreprocessorSymbols(tfmSymbol);
        ImmutableArray<MetadataReference> refs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        refs = refs
            .Add(MetadataReference.CreateFromFile(typeof(global::MessagePack.MessagePackObjectAttribute).Assembly.Location))
            .Add(MetadataReference.CreateFromFile(typeof(global::MessagePack.MessagePackSerializer).Assembly.Location));

        Compilation compilation = CSharpCompilation.Create(
            "ReceiptsTfmAssembly",
            new[] { CSharpSyntaxTree.ParseText(Source, parseOptions) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGenerators(compilation);
        ImmutableArray<SyntaxTree> generated = driver.GetRunResult().GeneratedTrees;

        var classes = generated
            .SelectMany(t => t.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Where(c => c.BaseList is not null && (c.BaseList.ToString().Contains("IMessagePackFormatter") || c.BaseList.ToString().Contains("IFormatterResolver")))
            .ToList();

        Assert.NotEmpty(classes);
        foreach (var c in classes)
        {
            bool has = c.AttributeLists.SelectMany(l => l.Attributes).Any(a => a.Name.ToString().Contains("ExcludeFromCodeCoverage"));
            Assert.True(has, $"With {tfmSymbol} defined, generated class '{c.Identifier.Text}' lacks an active [ExcludeFromCodeCoverage].");
        }
    }
}
