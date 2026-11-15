// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class Req2129GeneratedFormatterAttributesTests
{
    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.CSharp12);

    private const string ProjectBSource = @"
using MessagePack;
using MessagePack.Formatters;

namespace ProjectB
{
    [GeneratedMessagePackResolver]
    partial class MyResolver { }

    [GeneratedMessagePackFormatter]
    public partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject>
    {
    }
}
";

    [Fact]
    public async Task MessagePackObjectIgnoreMemberOnReferencedTypeIsHonoured()
    {
        string projectASource = @"
using MessagePack;

namespace ProjectA
{
    [MessagePackObject]
    public class MyObject
    {
        [Key(0)]
        public int Id { get; set; }

        [IgnoreMember]
        public string HiddenSecret { get; set; }
    }
}
";
        MetadataReference projectA = await CompileProjectAAsync(projectASource, referenceMessagePack: true);
        (Compilation output, ImmutableArray<Diagnostic> generatorDiagnostics, string generated) = await RunGeneratorAsync(projectA, ProjectBSource);

        AssertNoErrors(output, generatorDiagnostics);
        Assert.Contains("MyObjectFormatter", generated);
        Assert.DoesNotContain("HiddenSecret", generated);
    }

    [Fact]
    public async Task DataContractOnReferencedTypeIsHonoured()
    {
        string projectASource = @"
using System.Runtime.Serialization;

namespace ProjectA
{
    [DataContract]
    public class MyObject
    {
        [DataMember(Name = ""renamed_id"")]
        public int Id { get; set; }

        public string HiddenSecret { get; set; }
    }
}
";
        MetadataReference projectA = await CompileProjectAAsync(projectASource, referenceMessagePack: false);
        (Compilation output, ImmutableArray<Diagnostic> generatorDiagnostics, string generated) = await RunGeneratorAsync(projectA, ProjectBSource);

        AssertNoErrors(output, generatorDiagnostics);
        Assert.Contains("MyObjectFormatter", generated);
        Assert.DoesNotContain("HiddenSecret", generated);
    }

    private static async Task<MetadataReference> CompileProjectAAsync(string source, bool referenceMessagePack)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = new List<MetadataReference>(framework);
        if (referenceMessagePack)
        {
            references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        }

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static async Task<(Compilation Output, ImmutableArray<Diagnostic> GeneratorDiagnostics, string Generated)> RunGeneratorAsync(MetadataReference projectA, string source)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = new List<MetadataReference>(framework);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        references.Add(projectA);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new global::MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        GeneratorDriverRunResult result = driver.GetRunResult();
        foreach (GeneratorRunResult run in result.Results)
        {
            Assert.Null(run.Exception);
        }

        string generated = string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));
        return (output, generatorDiagnostics, generated);
    }

    private static void AssertNoErrors(Compilation output, ImmutableArray<Diagnostic> generatorDiagnostics)
    {
        List<Diagnostic> errors = output.GetDiagnostics()
            .Concat(generatorDiagnostics)
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }
}
