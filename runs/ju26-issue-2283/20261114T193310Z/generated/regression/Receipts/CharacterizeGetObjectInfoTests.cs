// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class CharacterizeGetObjectInfoTests
{
    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Latest);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GeneratesCompilingFormatterForMessagePackObject(bool keyAsPropertyName)
    {
        string body = keyAsPropertyName
            ? """
                [MessagePackObject(true)]
                public class MyObject
                {
                    public int A { get; set; }
                    public string B { get; set; }
                }
            """
            : """
                [MessagePackObject]
                public class MyObject
                {
                    [Key(0)]
                    public int A { get; set; }
                    [Key(1)]
                    public string B { get; set; }
                }
            """;
        string source = "using MessagePack;\nnamespace N\n{\n[GeneratedMessagePackResolver]\npartial class MyResolver { }\n" + body + "\n}\n";

        CSharpCompilation compilation = await CreateCompilationAsync(source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() }, parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> _);
        GeneratorDriverRunResult result = driver.GetRunResult();

        Assert.All(result.Results, r => Assert.Null(r.Exception));
        Assert.NotEmpty(result.GeneratedTrees);
        Assert.Empty(output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.True(HasFormatterFor(output, "N.MyObject"));
    }

    [Fact]
    public async Task UnattributedPublicPropertyIsReportedAtItsIdentifier()
    {
        string source = """
            using MessagePack;
            namespace N
            {
                [MessagePackObject]
                public class MyObject
                {
                    [Key(0)]
                    public int Attributed { get; set; }

                    public int Unattributed { get; set; }
                }
            }
            """;

        CSharpCompilation compilation = await CreateCompilationAsync(source);
        ImmutableArray<DiagnosticAnalyzer> analyzers = GetAnalyzers();
        Assert.NotEmpty(analyzers);
        ImmutableArray<Diagnostic> diagnostics = await compilation.WithAnalyzers(analyzers).GetAnalyzerDiagnosticsAsync();

        Assert.Contains(diagnostics, d =>
            d.Id.StartsWith("MsgPack", StringComparison.Ordinal) &&
            d.Location.IsInSource &&
            d.Location.SourceTree!.GetText().ToString(d.Location.SourceSpan) == "Unattributed");
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
