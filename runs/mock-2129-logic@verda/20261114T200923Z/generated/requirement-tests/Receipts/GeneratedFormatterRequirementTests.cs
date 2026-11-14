// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public sealed class GeneratedFormatterRequirementTests
{
    private const string FormatterSource = """
        using MessagePack;
        using MessagePack.Formatters;

        [GeneratedMessagePackFormatter]
        public partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject>;
        """;

    [Fact]
    public async Task BodylessFormatter_ReferencedType_UsesPublicReadWriteMembersContractlessly()
    {
        const string targetSource = """
            namespace ProjectA
            {
                public class MyObject
                {
                    public int Volume;
                    public string Label { get; set; }
                }
            }
            """;

        (GeneratorDriverRunResult runResult, Compilation outputCompilation) = await GenerateAsync(targetSource);
        AssertNoErrors(runResult, outputCompilation);

        string formatter = GetGeneratedFormatter(runResult);
        Assert.Contains(".Volume", formatter);
        Assert.Contains(".Label", formatter);
    }

    [Fact]
    public async Task Formatter_MessagePackAttributesOnReferencedType_AreHonored()
    {
        const string targetSource = """
            using MessagePack;

            namespace ProjectA
            {
                [MessagePackObject]
                public class MyObject
                {
                    [Key(0)]
                    public int Id { get; set; }
                }
            }
            """;

        (GeneratorDriverRunResult runResult, Compilation outputCompilation) = await GenerateAsync(targetSource, targetUsesMessagePackAttributes: true);
        AssertNoErrors(runResult, outputCompilation);

        string formatter = GetGeneratedFormatter(runResult);
        Assert.Contains("WriteArrayHeader", formatter);
        Assert.DoesNotContain("WriteMapHeader", formatter);
    }

    private static async Task<(GeneratorDriverRunResult RunResult, Compilation OutputCompilation)> GenerateAsync(
        string targetSource,
        bool targetUsesMessagePackAttributes = false)
    {
        var frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        IEnumerable<MetadataReference> messagePackReferences = ReferencesHelper.GetReferences(ReferencesSet.MessagePack);
        IEnumerable<MetadataReference> targetReferences = frameworkReferences;
        if (targetUsesMessagePackAttributes)
        {
            targetReferences = targetReferences.Concat(messagePackReferences);
        }

        MetadataReference targetReference = CompileAssembly("ProjectA", targetSource, targetReferences);
        IEnumerable<MetadataReference> formatterReferences = frameworkReferences.Concat(messagePackReferences).Append(targetReference);
        SyntaxTree formatterTree = CSharpSyntaxTree.ParseText(FormatterSource, new CSharpParseOptions(LanguageVersion.Latest));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectB",
            new[] { formatterTree },
            formatterReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation outputCompilation, out _);
        return (driver.GetRunResult(), outputCompilation);
    }

    private static MetadataReference CompileAssembly(string assemblyName, string source, IEnumerable<MetadataReference> references)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        Assert.True(emitResult.Success, string.Join("\n", emitResult.Diagnostics.Select(diagnostic => diagnostic.ToString())));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static void AssertNoErrors(GeneratorDriverRunResult runResult, Compilation outputCompilation)
    {
        Diagnostic[] errors = outputCompilation.GetDiagnostics()
            .Concat(runResult.Diagnostics)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.True(errors.Length == 0, string.Join("\n", errors.Select(diagnostic => diagnostic.ToString())));
    }

    private static string GetGeneratedFormatter(GeneratorDriverRunResult runResult)
    {
        string? formatter = runResult.GeneratedTrees
            .Select(tree => tree.GetText().ToString())
            .SingleOrDefault(text => text.Contains("partial class MyObjectFormatter", StringComparison.Ordinal));
        Assert.NotNull(formatter);
        return formatter!;
    }
}
