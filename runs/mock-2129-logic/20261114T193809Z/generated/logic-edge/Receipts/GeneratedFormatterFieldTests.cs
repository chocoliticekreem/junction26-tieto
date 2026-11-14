// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterFieldTests
{
    private readonly ITestOutputHelper logger;

    public GeneratedFormatterFieldTests(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    [Fact]
    public async Task ContractlessMode_SerializesMutableFields_AndCompiles()
    {
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);

        string projectA = """
            namespace ProjectA
            {
                public class MyType
                {
                    public int MutableField;
                    public readonly int ReadOnlyField;
                    public int Prop { get; set; }
                }
            }
            """;

        CSharpCompilation compilationA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(projectA, parseOptions) },
            framework,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        string projectB = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                public partial class MyTypeFormatter : IMessagePackFormatter<ProjectA.MyType> { }

                [GeneratedMessagePackResolver]
                partial class MyResolver { }
            }
            """;

        List<MetadataReference> referencesB = new(framework);
        referencesB.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        referencesB.Add(compilationA.ToMetadataReference());

        CSharpCompilation compilationB = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(projectB, parseOptions) },
            referencesB,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilationB, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);

        GeneratorDriverRunResult runResult = driver.GetRunResult();
        string generated = string.Join("\n", runResult.GeneratedTrees.Select(t => t.ToString()));
        this.logger.WriteLine(generated);

        List<Diagnostic> errors = output.GetDiagnostics()
            .Concat(generatorDiagnostics)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        foreach (Diagnostic error in errors)
        {
            this.logger.WriteLine(error.ToString());
        }

        Assert.Contains("MutableField", generated);
        Assert.Empty(errors);
    }
}
