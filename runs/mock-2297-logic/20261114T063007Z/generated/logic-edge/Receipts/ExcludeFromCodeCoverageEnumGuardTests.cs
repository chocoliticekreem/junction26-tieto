// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageEnumGuardTests
{
    private const string Source = """
        using MessagePack;

        namespace Test
        {
            public enum MyEnum { A, B }

            [MessagePackObject]
            public class MyObject
            {
                [Key(0)]
                public MyEnum Value { get; set; }
            }

            [GeneratedMessagePackResolver]
            partial class MyResolver { }
        }
        """;

    [Fact]
    public async Task EnumFormatter_HasExcludeFromCodeCoverage_OnModernTfm()
    {
        List<ClassDeclarationSyntax> formatters = await GenerateFormatterClassesAsync(Array.Empty<string>());
        List<ClassDeclarationSyntax> enumFormatters = formatters.Where(c => c.BaseList!.ToString().Contains("MyEnum")).ToList();
        Assert.NotEmpty(enumFormatters);
        foreach (ClassDeclarationSyntax c in enumFormatters)
        {
            Assert.True(HasExcludeFromCodeCoverage(c), $"Enum formatter '{c.Identifier.Text}' lacks an active [ExcludeFromCodeCoverage] attribute when no TFM symbols are defined.");
        }
    }

    [Fact]
    public async Task ObjectFormatter_HasExcludeFromCodeCoverage_OnNetStandard20()
    {
        List<ClassDeclarationSyntax> formatters = await GenerateFormatterClassesAsync(new[] { "NETSTANDARD2_0" });
        List<ClassDeclarationSyntax> objectFormatters = formatters.Where(c => c.BaseList!.ToString().Contains("MyObject")).ToList();
        Assert.NotEmpty(objectFormatters);
        foreach (ClassDeclarationSyntax c in objectFormatters)
        {
            Assert.True(HasExcludeFromCodeCoverage(c), $"Formatter '{c.Identifier.Text}' lacks an active [ExcludeFromCodeCoverage] attribute under NETSTANDARD2_0, although the attribute exists in netstandard2.0.");
        }
    }

    private static bool HasExcludeFromCodeCoverage(ClassDeclarationSyntax c)
    {
        return c.AttributeLists.SelectMany(l => l.Attributes).Any(a => a.Name.ToString().Contains("ExcludeFromCodeCoverage"));
    }

    private static async Task<List<ClassDeclarationSyntax>> GenerateFormatterClassesAsync(string[] preprocessorSymbols)
    {
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: preprocessorSymbols);
        var frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        IEnumerable<MetadataReference> references = frameworkRefs.Cast<MetadataReference>()
            .Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack).Cast<MetadataReference>());

        CSharpCompilation compilation = CSharpCompilation.Create(
            "ReceiptsTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(Source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGenerators(compilation);
        GeneratorDriverRunResult result = driver.GetRunResult();

        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        return result.GeneratedTrees
            .SelectMany(t => t.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
            .Where(c => c.BaseList is not null && c.BaseList.ToString().Contains("IMessagePackFormatter<"))
            .ToList();
    }
}
