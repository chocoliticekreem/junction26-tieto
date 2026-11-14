// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterLogicTests(ITestOutputHelper logger)
{
    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Latest);

    private static async Task<List<MetadataReference>> GetFrameworkReferencesAsync()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        return new List<MetadataReference>(framework);
    }

    private static MetadataReference EmitProjectA(string source, IEnumerable<MetadataReference> references)
    {
        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream ms = new();
        var emitResult = projectA.Emit(ms);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        return MetadataReference.CreateFromImage(ms.ToArray());
    }

    private (Compilation Output, ImmutableArray<Diagnostic> GeneratorDiagnostics, GeneratorDriverRunResult Run) RunGenerator(string source, IEnumerable<MetadataReference> references)
    {
        CSharpCompilation projectB = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(projectB, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        GeneratorDriverRunResult run = driver.GetRunResult();
        foreach (SyntaxTree tree in run.GeneratedTrees)
        {
            logger.WriteLine("// " + tree.FilePath);
            logger.WriteLine(tree.ToString());
        }

        foreach (Diagnostic d in generatorDiagnostics.Concat(output.GetDiagnostics()))
        {
            logger.WriteLine(d.ToString());
        }

        return (output, generatorDiagnostics, run);
    }

    [Fact]
    public async Task FormatterWithoutResolverDeclarationCompiles()
    {
        List<MetadataReference> framework = await GetFrameworkReferencesAsync();
        MetadataReference projectA = EmitProjectA(
            """
            namespace ProjectA
            {
                public class MyType
                {
                    public int Value { get; set; }
                    public string Name { get; set; }
                }
            }
            """,
            framework);

        List<MetadataReference> references = new(framework);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        references.Add(projectA);

        var result = RunGenerator(
            """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                public partial class MyTypeFormatter : IMessagePackFormatter<ProjectA.MyType>;
            }
            """,
            references);

        Assert.Empty(result.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(result.Output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public async Task AttributesOnFormattedTypeAreHonoured()
    {
        List<MetadataReference> framework = await GetFrameworkReferencesAsync();
        List<MetadataReference> withMessagePack = new(framework);
        withMessagePack.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        MetadataReference projectA = EmitProjectA(
            """
            using MessagePack;

            namespace ProjectA
            {
                [MessagePackObject]
                public class Keyed
                {
                    [Key(0)]
                    public int Value { get; set; }

                    [IgnoreMember]
                    public int IgnoredSecret { get; set; }
                }
            }
            """,
            withMessagePack);

        List<MetadataReference> references = new(withMessagePack);
        references.Add(projectA);

        var result = RunGenerator(
            """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver { }

                [GeneratedMessagePackFormatter]
                public partial class KeyedFormatter : IMessagePackFormatter<ProjectA.Keyed> { }
            }
            """,
            references);

        Assert.Empty(result.Output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        bool referencesIgnoredMember = result.Run.GeneratedTrees
            .SelectMany(t => t.GetRoot().DescendantNodes().OfType<IdentifierNameSyntax>())
            .Any(n => n.Identifier.ValueText == "IgnoredSecret");
        Assert.False(referencesIgnoredMember, "The generated formatter serializes a member marked [IgnoreMember] on the formatted type.");
    }
}
