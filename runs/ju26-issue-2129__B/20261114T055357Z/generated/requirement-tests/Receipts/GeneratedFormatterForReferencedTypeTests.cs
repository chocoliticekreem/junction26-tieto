// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterForReferencedTypeTests
{
    private const string ProjectBSource = """
        using MessagePack;
        using MessagePack.Formatters;

        namespace ProjectB
        {
            [GeneratedMessagePackFormatter]
            partial class MyTypeFormatter : IMessagePackFormatter<ProjectA.MyType>;

            [GeneratedMessagePackResolver]
            partial class MyResolver
            {
            }
        }
        """;

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp12);

    [Fact]
    public async Task BodylessFormatter_ForTypeInReferencedAssembly_IsGenerated()
    {
        MetadataReference projectA = await CompileProjectAAsync("""
            namespace ProjectA
            {
                public class MyType
                {
                    public int Identifier { get; set; }
                    public string Label { get; set; }
                }
            }
            """);

        (string generated, ImmutableArray<Diagnostic> errors) = await RunProjectBAsync(projectA, ProjectBSource);

        Assert.True(errors.IsEmpty, string.Join(Environment.NewLine, errors));
        Assert.Contains("MyTypeFormatter", generated);
        Assert.Contains("Serialize", generated);
    }

    [Fact]
    public async Task ContractlessType_SerializesPublicMembersOnly()
    {
        MetadataReference projectA = await CompileProjectAAsync("""
            namespace ProjectA
            {
                public class MyType
                {
                    private int hiddenCounter;
                    public int Identifier { get; set; }
                    public string Label { get; set; }
                    public int Peek() => hiddenCounter;
                }
            }
            """);

        (string generated, ImmutableArray<Diagnostic> errors) = await RunProjectBAsync(projectA, ProjectBSource);

        Assert.True(errors.IsEmpty, string.Join(Environment.NewLine, errors));
        Assert.Contains("Identifier", generated);
        Assert.Contains("Label", generated);
        Assert.DoesNotContain("hiddenCounter", generated);
    }

    [Fact]
    public async Task DataContractType_HonoursDataMemberAttributes()
    {
        MetadataReference projectA = await CompileProjectAAsync("""
            using System.Runtime.Serialization;

            namespace ProjectA
            {
                [DataContract]
                public class MyType
                {
                    [DataMember(Order = 0)]
                    public int Identifier { get; set; }

                    [DataMember(Order = 1)]
                    public string Label { get; set; }

                    public string Secret { get; set; }
                }
            }
            """);

        (string generated, ImmutableArray<Diagnostic> errors) = await RunProjectBAsync(projectA, ProjectBSource);

        Assert.True(errors.IsEmpty, string.Join(Environment.NewLine, errors));
        Assert.Contains("Identifier", generated);
        Assert.Contains("Label", generated);
        Assert.DoesNotContain("Secret", generated);
    }

    private static async Task<MetadataReference> CompileProjectAAsync(string source)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            framework,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        var emitResult = projectA.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static async Task<(string Generated, ImmutableArray<Diagnostic> Errors)> RunProjectBAsync(MetadataReference projectA, string source)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = Enumerable.Concat<MetadataReference>(framework, ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        references.Add(projectA);

        CSharpCompilation projectB = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(projectB, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);

        GeneratorDriverRunResult result = driver.GetRunResult();
        string generated = string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));
        ImmutableArray<Diagnostic> errors = generatorDiagnostics
            .Concat(output.GetDiagnostics())
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
        return (generated, errors);
    }
}
