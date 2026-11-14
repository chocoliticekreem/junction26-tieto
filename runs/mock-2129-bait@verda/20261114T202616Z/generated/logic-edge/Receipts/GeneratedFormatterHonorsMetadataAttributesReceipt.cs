// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterHonorsMetadataAttributesReceipt
{
    [Fact]
    public async Task MessagePackAttributesOnMetadataType_ControlKeysAndMembership()
    {
        const string projectASource = """
            using MessagePack;

            namespace ProjectA
            {
                [MessagePackObject]
                public class MyObject
                {
                    [Key(42)]
                    public int Value { get; set; }

                    [IgnoreMember]
                    public int Ignored { get; set; }
                }
            }
            """;

        const string projectBSource = """
            using MessagePack;
            using MessagePack.Formatters;
            using ProjectA;

            [GeneratedMessagePackFormatter]
            public partial class MyObjectFormatter : IMessagePackFormatter<MyObject>
            {
            }
            """;

        List<MetadataReference> projectAReferences = new();
        projectAReferences.AddRange(await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None));
        projectAReferences.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation projectA = CSharpCompilation.Create(
            assemblyName: "ProjectA",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(projectASource, new CSharpParseOptions(LanguageVersion.Latest)) },
            references: projectAReferences,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using MemoryStream projectAStream = new();
        var projectAEmitResult = projectA.Emit(projectAStream);
        projectAEmitResult.Success.Should().BeTrue();
        MetadataReference projectAReference = MetadataReference.CreateFromImage(projectAStream.ToArray());

        List<MetadataReference> projectBReferences = new(projectAReferences) { projectAReference };
        CSharpCompilation projectB = CSharpCompilation.Create(
            assemblyName: "ProjectB",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(projectBSource, new CSharpParseOptions(LanguageVersion.Latest)) },
            references: projectBReferences,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(
            projectB,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> generatorDiagnostics);

        generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();

        string generatedFormatter = outputCompilation.SyntaxTrees
            .Select(tree => tree.ToString())
            .Single(text => text.Contains("partial class MyObjectFormatter", StringComparison.Ordinal)
                && text.Contains("void Serialize", StringComparison.Ordinal));

        generatedFormatter.Should().NotContain("Ignored");
        generatedFormatter.Should().Contain("writer.Write(42)");
    }
}
