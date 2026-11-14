// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Receipts.GeneratedTests;

public class CharacterizeGetObjectInfoTests
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp7_3);

    private static async Task<CSharpCompilation> CreateCompilationAsync(string source)
    {
        ImmutableArray<MetadataReference> framework = await Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        ImmutableArray<MetadataReference> references = framework
            .Add(MetadataReference.CreateFromFile(typeof(MessagePack.MessagePackObjectAttribute).Assembly.Location))
            .Add(MetadataReference.CreateFromFile(typeof(MessagePack.MessagePackSerializer).Assembly.Location));
        return CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static (Compilation Output, GeneratorDriverRunResult Run) RunGenerator(CSharpCompilation compilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator()).WithUpdatedParseOptions(ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> _);
        return (output, driver.GetRunResult());
    }

    private static ImmutableArray<DiagnosticAnalyzer> LoadAnalyzers()
    {
        return typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract
                && typeof(DiagnosticAnalyzer).IsAssignableFrom(t)
                && t.GetCustomAttributes(typeof(DiagnosticAnalyzerAttribute), false).Length > 0
                && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();
    }

    private static async Task<ImmutableArray<Diagnostic>> RunAnalyzersAsync(CSharpCompilation compilation)
    {
        ImmutableArray<DiagnosticAnalyzer> analyzers = LoadAnalyzers();
        Assert.NotEmpty(analyzers);
        CompilationWithAnalyzers withAnalyzers = compilation.WithAnalyzers(analyzers);
        return await withAnalyzers.GetAnalyzerDiagnosticsAsync(CancellationToken.None);
    }

    private static string SpanText(Diagnostic d)
    {
        if (d.Location.SourceTree is null)
        {
            return string.Empty;
        }

        return d.Location.SourceTree.GetText().ToString(d.Location.SourceSpan);
    }

    [Fact]
    public async Task IntKeyedObject_GeneratesCompilingFormatter()
    {
        string source = """
            using MessagePack;

            namespace MyNs
            {
                [MessagePackObject]
                public class MyObject
                {
                    [Key(0)]
                    public int FirstValue { get; set; }

                    [Key(1)]
                    public string SecondValue { get; set; }
                }

                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }
            }
            """;

        CSharpCompilation compilation = await CreateCompilationAsync(source);
        (Compilation output, GeneratorDriverRunResult run) = RunGenerator(compilation);

        Assert.All(run.Results, r => Assert.Null(r.Exception));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        string[] generated = run.Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()).ToArray();
        Assert.Contains(generated, text => text.Contains("IMessagePackFormatter<") && text.Contains("MyObject") && text.Contains("FirstValue") && text.Contains("SecondValue"));
        Assert.Contains(run.Results.SelectMany(r => r.GeneratedSources), s => s.HintName.Contains("MyResolver"));
    }

    [Fact]
    public async Task UnkeyedPublicProperty_ReportsDiagnosticAtPropertyIdentifier()
    {
        string source = """
            using MessagePack;

            namespace MyNs
            {
                [MessagePackObject]
                public class MyObject
                {
                    [Key(0)]
                    public int Keyed { get; set; }

                    public int Unkeyed { get; set; }
                }
            }
            """;

        CSharpCompilation compilation = await CreateCompilationAsync(source);
        ImmutableArray<Diagnostic> diagnostics = await RunAnalyzersAsync(compilation);

        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.Contains(diagnostics, d => d.Id.StartsWith("MsgPack", StringComparison.Ordinal) && SpanText(d) == "Unkeyed");
        Assert.DoesNotContain(diagnostics, d => d.Id.StartsWith("MsgPack", StringComparison.Ordinal) && SpanText(d) == "Keyed");
    }

    [Fact]
    public async Task MapModeObject_NoWarningsAndGeneratesFormatterForAllPublicMembers()
    {
        string source = """
            using MessagePack;

            namespace MyNs
            {
                [MessagePackObject(true)]
                public class Person
                {
                    public string PersonName { get; set; }

                    public int PersonAge { get; set; }
                }

                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }
            }
            """;

        CSharpCompilation compilation = await CreateCompilationAsync(source);
        ImmutableArray<Diagnostic> diagnostics = await RunAnalyzersAsync(compilation);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AD0001");
        Assert.DoesNotContain(diagnostics, d => d.Id.StartsWith("MsgPack", StringComparison.Ordinal) && d.Severity >= DiagnosticSeverity.Warning);

        (Compilation output, GeneratorDriverRunResult run) = RunGenerator(compilation);
        Assert.All(run.Results, r => Assert.Null(r.Exception));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        string[] generated = run.Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()).ToArray();
        Assert.Contains(generated, text => text.Contains("IMessagePackFormatter<") && text.Contains("Person") && text.Contains("PersonName") && text.Contains("PersonAge"));
    }
}
