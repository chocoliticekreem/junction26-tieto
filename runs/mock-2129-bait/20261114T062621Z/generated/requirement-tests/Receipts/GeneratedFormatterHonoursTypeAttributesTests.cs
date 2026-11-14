// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterHonoursTypeAttributesTests(ITestOutputHelper logger)
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp12);

    private const string ProjectASource = """
        using MessagePack;

        namespace ProjectA
        {
            [MessagePackObject]
            public class Keyed
            {
                [Key("custom_name")]
                public string Name { get; set; }

                [IgnoreMember]
                public string Secret { get; set; }
            }
        }
        """;

    private const string ProjectBSource = """
        using MessagePack;
        using MessagePack.Formatters;

        namespace ProjectB
        {
            [GeneratedMessagePackFormatter]
            public partial class KeyedFormatter : IMessagePackFormatter<ProjectA.Keyed>
            {
            }

            [GeneratedMessagePackResolver]
            partial class MyResolver
            {
            }
        }
        """;

    [Fact]
    public async Task IgnoredMemberOfAttributedTypeIsNotSerialized()
    {
        (string generated, ImmutableArray<Diagnostic> errors) = await RunAsync();
        logger.WriteLine(generated);
        Assert.True(errors.IsEmpty, string.Join(Environment.NewLine, errors));
        Assert.Contains("KeyedFormatter", generated);
        Assert.DoesNotContain("Secret", generated);
    }

    [Fact]
    public async Task StringKeyOfAttributedTypeIsHonoured()
    {
        (string generated, ImmutableArray<Diagnostic> errors) = await RunAsync();
        logger.WriteLine(generated);
        Assert.True(errors.IsEmpty, string.Join(Environment.NewLine, errors));
        Assert.Contains("KeyedFormatter", generated);
        Assert.Contains("custom_name", generated);
    }

    private static async Task<(string Generated, ImmutableArray<Diagnostic> Errors)> RunAsync()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(ProjectASource, ParseOptions) },
            framework,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddReferences(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        using MemoryStream stream = new();
        var emitResult = projectA.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference projectARef = MetadataReference.CreateFromImage(stream.ToArray());

        CSharpCompilation projectB = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(ProjectBSource, ParseOptions) },
            framework.Add(projectARef),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddReferences(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(projectB, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        string generated = string.Join("\n", driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()));
        ImmutableArray<Diagnostic> errors = generatorDiagnostics.Concat(output.GetDiagnostics())
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
        return (generated, errors);
    }
}
