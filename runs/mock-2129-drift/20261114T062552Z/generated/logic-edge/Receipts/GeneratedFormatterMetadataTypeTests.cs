// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterMetadataTypeTests
{
    private readonly ITestOutputHelper logger;

    public GeneratedFormatterMetadataTypeTests(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    [Fact]
    public async Task FormatterForTypeFromCompiledReferencedAssembly_IsGenerated()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.CSharp12);

        string libSource = """
            namespace ProjectA
            {
                public class MyType
                {
                    public int Value { get; set; }
                    public string Name { get; set; }
                }
            }
            """;

        CSharpCompilation libCompilation = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(libSource, parseOptions) },
            framework,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using MemoryStream ms = new MemoryStream();
        var emitResult = libCompilation.Emit(ms);
        emitResult.Success.Should().BeTrue();
        MetadataReference libReference = MetadataReference.CreateFromImage(ms.ToArray());

        string source = """
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                partial class MyTypeFormatter : IMessagePackFormatter<ProjectA.MyType>
                {
                }

                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }
            }
            """;

        List<MetadataReference> references = framework
            .Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack).Cast<MetadataReference>())
            .ToList();
        references.Add(libReference);

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);

        foreach (Diagnostic d in generatorDiagnostics)
        {
            this.logger.WriteLine("generator: " + d.ToString());
        }

        List<Diagnostic> errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        foreach (Diagnostic d in errors)
        {
            this.logger.WriteLine("compile: " + d.ToString());
        }

        generatorDiagnostics.Select(d => d.Id).Should().NotContain("MsgPack021");
        errors.Should().BeEmpty();
    }
}
