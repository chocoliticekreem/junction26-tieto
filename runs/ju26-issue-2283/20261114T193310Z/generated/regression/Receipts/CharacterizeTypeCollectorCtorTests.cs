// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class CharacterizeTypeCollectorCtorTests
{
    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Latest);

    [Fact]
    public async Task MessagePackObjectStructIsAcceptedAndGetsFormatter()
    {
        string source = """
            using MessagePack;
            namespace N
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver { }

                [MessagePackObject]
                public struct MyStruct
                {
                    [Key(0)]
                    public int X;
                }
            }
            """;

        CSharpCompilation compilation = await CreateCompilationAsync(source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() }, parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> _);
        GeneratorDriverRunResult result = driver.GetRunResult();

        Assert.All(result.Results, r => Assert.Null(r.Exception));
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.True(HasFormatterFor(output, "N.MyStruct"));
    }

    [Fact]
    public async Task InaccessibleDataTypeIsReportedAndNotFormatted()
    {
        string source = """
            using MessagePack;

            [GeneratedMessagePackResolver]
            partial class MyResolver { }

            class Outer
            {
                [MessagePackObject]
                class MyObject
                {
                    [Key(0)]
                    public int Value { get; set; }
                }
            }
            """;

        CSharpCompilation compilation = await CreateCompilationAsync(source);
        ImmutableArray<DiagnosticAnalyzer> analyzers = GetAnalyzers();
        Assert.NotEmpty(analyzers);
        ImmutableArray<Diagnostic> diagnostics = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();

        Assert.Contains(diagnostics, d =>
            d.Id == "MsgPack012" &&
            d.Location.IsInSource &&
            d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan) == "MyObject");

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() }, parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> _);
        Assert.All(driver.GetRunResult().Results, r => Assert.Null(r.Exception));
        Assert.False(HasFormatterFor(output, "Outer+MyObject"));
    }

    private static ImmutableArray<DiagnosticAnalyzer> GetAnalyzers()
    {
        return typeof(MessagePack.SourceGenerator.MessagePackGenerator).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(t) && t.GetCustomAttribute<DiagnosticAnalyzerAttribute>() is not null && t.GetConstructor(Type.EmptyTypes) is not null)
            .Select(t => (DiagnosticAnalyzer)Activator.CreateInstance(t)!)
            .ToImmutableArray();
    }

    private static async Task<CSharpCompilation> CreateCompilationAsync(string source)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        IEnumerable<MetadataReference> references = framework.Concat<MetadataReference>(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        return CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, ParseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
    }

    private static bool HasFormatterFor(Compilation output, string metadataName)
    {
        INamedTypeSymbol? target = output.GetTypeByMetadataName(metadataName);
        INamedTypeSymbol? formatterInterface = output.GetTypeByMetadataName("MessagePack.Formatters.IMessagePackFormatter`1");
        Assert.NotNull(target);
        Assert.NotNull(formatterInterface);
        INamedTypeSymbol constructed = formatterInterface!.Construct(target!);
        return GetAllTypes(output.Assembly.GlobalNamespace).Any(t => t.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, constructed)));
    }

    private static IEnumerable<INamedTypeSymbol> GetAllTypes(INamespaceOrTypeSymbol container)
    {
        foreach (ISymbol member in container.GetMembers())
        {
            if (member is INamespaceSymbol ns)
            {
                foreach (INamedTypeSymbol t in GetAllTypes(ns))
                {
                    yield return t;
                }
            }
            else if (member is INamedTypeSymbol type)
            {
                yield return type;
                foreach (INamedTypeSymbol t in GetAllTypes(type))
                {
                    yield return t;
                }
            }
        }
    }
}
