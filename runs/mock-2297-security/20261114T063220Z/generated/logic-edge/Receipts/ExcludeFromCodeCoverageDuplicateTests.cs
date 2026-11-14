// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageDuplicateTests
{
    [Fact]
    public async Task UserAttributeOnResolver_NoDuplicateAttributeError()
    {
        string source = """
            using System.Diagnostics.CodeAnalysis;
            using MessagePack;

            namespace N
            {
                [GeneratedMessagePackResolver]
                [ExcludeFromCodeCoverage]
                partial class MyResolver { }

                [MessagePackObject]
                public class MyObject
                {
                    [Key(0)]
                    public int Value { get; set; }
                }
            }
            """;

        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = new List<MetadataReference>(framework);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);

        Assert.True(output.SyntaxTrees.Count() > 1, "generator produced no sources");
        List<Diagnostic> duplicates = output.GetDiagnostics().Where(d => d.Id == "CS0579").ToList();
        Assert.True(duplicates.Count == 0, string.Join(Environment.NewLine, duplicates.Select(d => d.ToString())));
    }
}
