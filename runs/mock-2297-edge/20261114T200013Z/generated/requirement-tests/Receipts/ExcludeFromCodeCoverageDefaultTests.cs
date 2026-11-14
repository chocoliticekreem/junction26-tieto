// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageDefaultTests
{
    private const string Source = """
        using MessagePack;

        namespace TestNs
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
                public MyEnum Kind { get; set; }
            }

            [MessagePackObject]
            public class MyMapObject
            {
                [Key("name")]
                public string Name { get; set; }
            }
        }
        """;

    [Fact]
    public async Task GeneratedFormattersAndResolver_AreExcludedFromCodeCoverage_ByDefault()
    {
        var (output, generated) = await RunGeneratorAsync();

        var formatters = GeneratedClasses(generated, "IMessagePackFormatter").ToList();
        var resolvers = GeneratedClasses(generated, "IFormatterResolver").ToList();

        Assert.NotEmpty(formatters);
        Assert.NotEmpty(resolvers);

        foreach (var c in formatters.Concat(resolvers))
        {
            Assert.True(HasExcludeFromCodeCoverage(c), $"Generated class '{c.Identifier.Text}' lacks [ExcludeFromCodeCoverage].");
        }

        var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors.Select(e => e.ToString())));
    }

    private static async Task<(Compilation Output, ImmutableArray<SyntaxTree> Generated)> RunGeneratorAsync()
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        ImmutableArray<MetadataReference> refs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        refs = refs
            .Add(MetadataReference.CreateFromFile(typeof(global::MessagePack.MessagePackObjectAttribute).Assembly.Location))
            .Add(MetadataReference.CreateFromFile(typeof(global::MessagePack.MessagePackSerializer).Assembly.Location));

        Compilation compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(Source, parseOptions) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
        return (output, driver.GetRunResult().GeneratedTrees);
    }

    private static IEnumerable<ClassDeclarationSyntax> GeneratedClasses(ImmutableArray<SyntaxTree> trees, string baseMarker)
    {
        return trees
            .SelectMany(t => t.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Where(c => c.BaseList is not null && c.BaseList.ToString().Contains(baseMarker));
    }

    private static bool HasExcludeFromCodeCoverage(ClassDeclarationSyntax c)
    {
        return c.AttributeLists.SelectMany(l => l.Attributes).Any(a => a.Name.ToString().Contains("ExcludeFromCodeCoverage"));
    }
}
