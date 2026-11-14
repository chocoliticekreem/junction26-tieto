// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterHonorsAttributesTests
{
    [Fact]
    public async Task MessagePackAttributesOnReferencedTypeAreHonored()
    {
        ImmutableArray<MetadataReference> frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        MetadataReference[] messagePackReferences = frameworkReferences
            .Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack))
            .ToArray();

        const string projectASource = """
            using MessagePack;

            namespace ProjectA
            {
                [MessagePackObject]
                public class MyObject
                {
                    [Key(0)]
                    public int Value { get; set; }

                    [IgnoreMember]
                    public int Ignored { get; set; }
                }
            }
            """;

        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(projectASource, new CSharpParseOptions(LanguageVersion.Latest)) },
            messagePackReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream assemblyImage = new();
        var emitResult = projectA.Emit(assemblyImage);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference projectAReference = MetadataReference.CreateFromImage(assemblyImage.ToArray());

        const string projectBSource = """
            using MessagePack;
            using MessagePack.Formatters;

            [GeneratedMessagePackFormatter]
            public partial class MyObjectFormatter : IMessagePackFormatter<ProjectA.MyObject>
            {
            }
            """;

        CSharpCompilation projectB = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(projectBSource, new CSharpParseOptions(LanguageVersion.Latest)) },
            messagePackReferences.Concat(new[] { projectAReference }),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(
            projectB,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> generatorDiagnostics);

        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Empty(outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        string? formatterSource = driver.GetRunResult().Results
            .SelectMany(result => result.GeneratedSources)
            .Select(source => source.SourceText.ToString())
            .SingleOrDefault(source => source.Contains("partial class MyObjectFormatter", StringComparison.Ordinal));

        Assert.True(formatterSource is not null, "The user-declared formatter implementation was not generated.");
        Assert.Contains("ReadArrayHeader", formatterSource!);
        Assert.DoesNotContain("Ignored", formatterSource!);
    }
}
