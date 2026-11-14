// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedMessagePackFormatterAttributeHonouringReceiptTests
{
    [Fact]
    public async Task MessagePackAttributesOnReferencedTypeAreHonoured()
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp12);
        var frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var messagePackReferences = ReferencesHelper.GetReferences(ReferencesSet.MessagePack);

        const string projectASource = """
            using MessagePack;

            namespace ProjectA;

            [MessagePackObject]
            public class MyObject
            {
                [Key(0)]
                public int Id { get; set; }

                [IgnoreMember]
                public string Secret { get; set; }
            }
            """;

        var projectA = CSharpCompilation.Create(
            assemblyName: "ProjectA",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(projectASource, parseOptions) },
            references: frameworkReferences.Concat(messagePackReferences),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        MetadataReference projectAReference = EmitReference(projectA);

        const string projectBSource = """
            using MessagePack;
            using MessagePack.Formatters;

            [GeneratedMessagePackFormatter]
            public partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject>
            {
            }

            [GeneratedMessagePackResolver]
            public partial class ProjectBResolver
            {
            }
            """;

        var projectB = CSharpCompilation.Create(
            assemblyName: "ProjectB",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(projectBSource, parseOptions) },
            references: frameworkReferences
                .Concat(messagePackReferences)
                .Append(projectAReference),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(
            projectB,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> generatorDiagnostics);

        AssertNoErrors(generatorDiagnostics);
        AssertNoErrors(outputCompilation.GetDiagnostics());

        string formatterSource = GetFormatterSource(driver);
        Assert.Contains("WriteArrayHeader", formatterSource);
        Assert.DoesNotContain("WriteMapHeader", formatterSource);
        Assert.DoesNotContain("\"Secret\"", formatterSource);
    }

    private static MetadataReference EmitReference(Compilation compilation)
    {
        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static string GetFormatterSource(GeneratorDriver driver)
    {
        return driver.GetRunResult().GeneratedTrees
            .Select(tree => tree.ToString())
            .SingleOrDefault(text => text.Contains("partial class MyObjectFormatter", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("No implementation was generated for MyObjectFormatter.");
    }

    private static void AssertNoErrors(IEnumerable<Diagnostic> diagnostics)
    {
        Diagnostic[] errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(d => d.ToString())));
    }
}
