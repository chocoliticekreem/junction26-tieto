// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageFormatterTests
{
    private const string Source = """
        using MessagePack;

        namespace TestNs
        {
            [GeneratedMessagePackResolver]
            partial class MyResolver { }

            [MessagePackObject]
            public class IntKeyed { [Key(0)] public int A { get; set; } }

            [MessagePackObject]
            public class StringKeyed { [Key("a")] public int A { get; set; } }

            public enum MyEnum { A, B }

            [MessagePackObject]
            public class HasEnum { [Key(0)] public MyEnum E { get; set; } }

            [Union(0, typeof(UnionA))]
            public interface IUnion { }

            [MessagePackObject]
            public class UnionA : IUnion { [Key(0)] public int X { get; set; } }

            [MessagePackObject]
            public class HasUnion { [Key(0)] public IUnion U { get; set; } }
        }
        """;

    [Fact]
    public async Task FormatterTypes_AreDecorated_WithDefaultOptions()
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = frameworkRefs.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        var compilation = CSharpCompilation.Create(
            "ReceiptsTest",
            new[] { CSharpSyntaxTree.ParseText(Source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var generator = new MessagePack.SourceGenerator.MessagePackGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { generator.AsSourceGenerator() }, parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        var failures = new List<string>();
        int formatterCount = 0;
        foreach (var tree in driver.GetRunResult().GeneratedTrees)
        {
            foreach (var type in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (type.BaseList is null || !type.BaseList.ToString().Contains("IMessagePackFormatter"))
                {
                    continue;
                }

                formatterCount++;
                bool covered = type.AncestorsAndSelf().OfType<TypeDeclarationSyntax>()
                    .Any(t => t.AttributeLists.SelectMany(l => l.Attributes).Any(a => a.Name.ToString().Contains("ExcludeFromCodeCoverage")));
                if (!covered)
                {
                    failures.Add($"{tree.FilePath}: {type.Identifier.Text}");
                }
            }
        }

        Assert.True(formatterCount > 0, "No generated formatter types found.");
        Assert.True(failures.Count == 0, "Undecorated formatters: " + string.Join(", ", failures));
    }
}
