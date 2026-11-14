// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterRequirementGapTests
{
    private const string ProjectBSource = """
        using MessagePack;
        using MessagePack.Formatters;

        namespace ProjectB;

        [GeneratedMessagePackResolver]
        partial class MyResolver { }

        [GeneratedMessagePackFormatter]
        partial class MyTypeFormatter : IMessagePackFormatter<ProjectA.MyType>;
        """;

    [Fact]
    public async Task PublicWritableField_IsSerialized_AndReadOnlyFieldIsNot()
    {
        string projectA = """
            namespace ProjectA;

            public class MyType
            {
                public int Quantity;

                public readonly int Frozen;

                public int Identifier { get; set; }
            }
            """;

        (string generated, ImmutableArray<Diagnostic> errors) = await RunAsync(projectA, referenceMessagePackFromA: false, ProjectBSource);

        Assert.True(errors.IsEmpty, string.Join(Environment.NewLine, errors));
        Assert.Contains("Quantity", generated);
    }

    [Fact]
    public async Task MessagePackAttributesOnReferencedType_AreHonoured()
    {
        string projectA = """
            using MessagePack;

            namespace ProjectA;

            [MessagePackObject]
            public class MyType
            {
                [Key(0)]
                public int Identifier { get; set; }

                [IgnoreMember]
                public string? SecretValue { get; set; }
            }
            """;

        (string generated, ImmutableArray<Diagnostic> errors) = await RunAsync(projectA, referenceMessagePackFromA: true, ProjectBSource);

        Assert.True(errors.IsEmpty, string.Join(Environment.NewLine, errors));
        Assert.Contains("MyTypeFormatter", generated);
        Assert.DoesNotContain("SecretValue", generated);
    }

    private static async Task<(string Generated, ImmutableArray<Diagnostic> Errors)> RunAsync(string projectASource, bool referenceMessagePackFromA, string projectBSource)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.CSharp12);
        CSharpCompilationOptions compilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable);

        List<MetadataReference> referencesA = new List<MetadataReference>(framework);
        if (referenceMessagePackFromA)
        {
            referencesA.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        }

        CSharpCompilation projectA = CSharpCompilation.Create("ProjectA", new[] { CSharpSyntaxTree.ParseText(projectASource, parseOptions) }, referencesA, compilationOptions);
        using MemoryStream stream = new MemoryStream();
        var emitResult = projectA.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference projectARef = MetadataReference.CreateFromImage(stream.ToArray());

        List<MetadataReference> referencesB = new List<MetadataReference>(framework);
        referencesB.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        referencesB.Add(projectARef);
        CSharpCompilation projectB = CSharpCompilation.Create("ProjectB", new[] { CSharpSyntaxTree.ParseText(projectBSource, parseOptions) }, referencesB, compilationOptions);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new ISourceGenerator[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(projectB, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);

        string generated = string.Join("\n", driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()));
        ImmutableArray<Diagnostic> errors = output.GetDiagnostics().Concat(generatorDiagnostics).Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray();
        return (generated, errors);
    }
}
