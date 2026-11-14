// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterReceiptsTests
{
    private const string AppSource = """
        using MessagePack;
        using MessagePack.Formatters;

        namespace ProjectB
        {
            [GeneratedMessagePackFormatter]
            public partial class DataFormatter : IMessagePackFormatter<ProjectA.Data> { }

            [GeneratedMessagePackResolver]
            partial class MyResolver { }
        }
        """;

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    [Fact]
    public async Task Contractless_PublicMutableField_IsSerialized()
    {
        string lib = """
            namespace ProjectA
            {
                public class Data
                {
                    public int MutableField;
                    public int Prop { get; set; }
                }
            }
            """;
        (Compilation _, string? formatterSource) = await RunAsync(lib);
        Assert.NotNull(formatterSource);
        Assert.Contains("MutableField", formatterSource);
    }

    [Fact]
    public async Task Contractless_ReadOnlyField_DoesNotBreakGeneratedFormatter()
    {
        string lib = """
            namespace ProjectA
            {
                public class Data
                {
                    public readonly int ReadOnlyField;
                    public int Prop { get; set; }
                }
            }
            """;
        (Compilation output, string? formatterSource) = await RunAsync(lib);
        Assert.NotNull(formatterSource);
        List<Diagnostic> errors = output.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Location.SourceTree is not null && d.Location.SourceTree.ToString().Contains("partial class DataFormatter"))
            .ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors.Select(e => e.ToString())));
    }

    [Fact]
    public async Task AttributedType_IgnoreMember_IsHonoured()
    {
        string lib = """
            using MessagePack;

            namespace ProjectA
            {
                [MessagePackObject]
                public class Data
                {
                    [Key(0)]
                    public int Value { get; set; }

                    [IgnoreMember]
                    public int Hidden { get; set; }
                }
            }
            """;
        (Compilation _, string? formatterSource) = await RunAsync(lib);
        Assert.NotNull(formatterSource);
        Assert.DoesNotContain("Hidden", formatterSource);
    }

    private static async Task<(Compilation Output, string? FormatterSource)> RunAsync(string libSource)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> refs = new(framework);
        refs.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        CSharpCompilation lib = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(libSource, ParseOptions) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream ms = new();
        var emitResult = lib.Emit(ms);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics.Select(d => d.ToString())));

        List<MetadataReference> appRefs = new(refs) { MetadataReference.CreateFromImage(ms.ToArray()) };
        CSharpCompilation app = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(AppSource, ParseOptions) },
            appRefs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(app, out Compilation output, out ImmutableArray<Diagnostic> _);

        string? formatterSource = output.SyntaxTrees
            .Select(t => t.ToString())
            .FirstOrDefault(s => s.Contains("partial class DataFormatter"));
        return (output, formatterSource);
    }
}
