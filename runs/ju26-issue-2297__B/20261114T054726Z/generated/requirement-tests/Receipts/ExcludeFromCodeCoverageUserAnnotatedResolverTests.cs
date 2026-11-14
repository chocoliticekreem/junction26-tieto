// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageUserAnnotatedResolverTests(ITestOutputHelper logger)
{
    [Fact]
    public async Task UserAnnotatedResolver_StillCompiles()
    {
        string source = """
            using System.Diagnostics.CodeAnalysis;
            using MessagePack;

            namespace TestNs
            {
                [ExcludeFromCodeCoverage]
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

        var references = new List<MetadataReference>();
        references.AddRange(await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None));
        references.Add(MetadataReference.CreateFromFile(typeof(global::MessagePack.MessagePackSerializer).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(global::MessagePack.MessagePackObjectAttribute).Assembly.Location));

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsUserAnnotatedAssembly",
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new global::MessagePack.SourceGenerator.MessagePackGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);

        Assert.DoesNotContain(generatorDiagnostics, d => d.Severity == DiagnosticSeverity.Error);

        List<Diagnostic> errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        foreach (Diagnostic e in errors)
        {
            logger.WriteLine(e.ToString());
        }

        Assert.Empty(errors);
    }
}
