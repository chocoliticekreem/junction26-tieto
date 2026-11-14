// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Receipts.GeneratedTests;

public class StringKeyedMembersFormatterReceiptTests
{
    [Fact]
    public void StringKeyedPropertyAndFieldAreBothInGeneratedFormatter()
    {
        string source = """
            using MessagePack;

            namespace TestNs
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }

                [MessagePackObject]
                public class StringKeyed
                {
                    [Key("pkey")]
                    public int PropAlpha { get; set; }

                    [Key("fkey")]
                    public int FieldBeta;
                }
            }
            """;

        CSharpCompilation compilation = CreateCompilation(source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator());
        driver = driver.RunGenerators(compilation);
        GeneratorDriverRunResult result = driver.GetRunResult();

        Assert.All(result.Results, r => Assert.Null(r.Exception));
        string generated = string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));
        Assert.Contains("StringKeyed", generated);
        Assert.Contains("PropAlpha", generated);
        Assert.Contains("FieldBeta", generated);
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        foreach (string path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string name = Path.GetFileName(path);
            if (name.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
                || name.Equals("netstandard.dll", StringComparison.OrdinalIgnoreCase)
                || name.Equals("mscorlib.dll", StringComparison.OrdinalIgnoreCase))
            {
                paths.Add(path);
            }
        }

        paths.Add(typeof(MessagePack.MessagePackObjectAttribute).Assembly.Location);
        paths.Add(typeof(MessagePack.MessagePackSerializer).Assembly.Location);

        IEnumerable<MetadataReference> references = paths.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp7_3));
        return CSharpCompilation.Create(
            "ReceiptsStringKeyAssembly",
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
