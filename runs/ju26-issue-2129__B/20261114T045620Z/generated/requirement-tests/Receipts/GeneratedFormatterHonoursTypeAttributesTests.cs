// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterHonoursTypeAttributesTests
{
    private const string ProjectASource = """
        using MessagePack;

        namespace ProjectA
        {
            [MessagePackObject]
            public class Keyed
            {
                [Key(0)]
                public int Id { get; set; }

                [Key(1)]
                public string Name { get; set; }

                [IgnoreMember]
                public int Hidden { get; set; }
            }

            [MessagePackObject(keyAsPropertyName: true)]
            public class Named
            {
                public int Id { get; set; }

                public string Name { get; set; }

                [IgnoreMember]
                public int Hidden { get; set; }
            }
        }
        """;

    private readonly ITestOutputHelper logger;

    public GeneratedFormatterHonoursTypeAttributesTests(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    [Fact]
    public async Task IntKeyedReferencedType_UsesItsKeyAttributes()
    {
        string json = await this.CompileAndSerializeAsync("Keyed");
        Assert.Equal("[7,\"x\"]", json);
    }

    [Fact]
    public async Task ReferencedTypeWithIgnoreMember_DoesNotSerializeIgnoredMember()
    {
        string json = await this.CompileAndSerializeAsync("Named");
        Assert.Contains("\"Id\":7", json);
        Assert.DoesNotContain("Hidden", json);
    }

    private static byte[] Emit(Compilation compilation, ITestOutputHelper logger)
    {
        using MemoryStream stream = new();
        var result = compilation.Emit(stream);
        foreach (Diagnostic d in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
        {
            logger.WriteLine(d.ToString());
        }

        Assert.True(result.Success, "Emit failed for " + compilation.AssemblyName);
        return stream.ToArray();
    }

    private async Task<string> CompileAndSerializeAsync(string typeName)
    {
        CSharpParseOptions parseOptions = new(LanguageVersion.CSharp12);
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        MetadataReference annotations = MetadataReference.CreateFromFile(typeof(MessagePack.KeyAttribute).Assembly.Location);

        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(ProjectASource, parseOptions) },
            framework.Add(annotations),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        byte[] imageA = Emit(projectA, this.logger);

        string projectBSource = $$"""
            using MessagePack;
            using MessagePack.Formatters;
            using MessagePack.Resolvers;

            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                public partial class TheFormatter : IMessagePackFormatter<ProjectA.{{typeName}}>
                {
                }

                [GeneratedMessagePackResolver]
                internal partial class MyResolver
                {
                }

                public static class Probe
                {
                    public static string ToJson(ProjectA.{{typeName}} value)
                    {
                        IFormatterResolver resolver = CompositeResolver.Create(
                            new IMessagePackFormatter[] { new TheFormatter() },
                            new IFormatterResolver[] { StandardResolver.Instance });
                        MessagePackSerializerOptions options = MessagePackSerializerOptions.Standard.WithResolver(resolver);
                        byte[] bytes = MessagePackSerializer.Serialize(value, options);
                        return MessagePackSerializer.ConvertToJson(bytes);
                    }
                }
            }
            """;

        MetadataReference[] references = framework.AddRange(new MetadataReference[]
        {
            MetadataReference.CreateFromImage(imageA),
            MetadataReference.CreateFromFile(typeof(MessagePack.MessagePackSerializer).Assembly.Location),
            annotations,
        }).ToArray();

        CSharpCompilation projectB = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(projectBSource, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(projectB, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);

        foreach (SyntaxTree tree in output.SyntaxTrees)
        {
            this.logger.WriteLine(tree.ToString());
        }

        foreach (Diagnostic d in generatorDiagnostics)
        {
            this.logger.WriteLine(d.ToString());
        }

        Assert.Empty(generatorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        byte[] imageB = Emit(output, this.logger);

        AssemblyLoadContext alc = new("receipts-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            Assembly a = alc.LoadFromStream(new MemoryStream(imageA));
            alc.Resolving += (context, name) => name.Name == "ProjectA" ? a : null;
            Assembly b = alc.LoadFromStream(new MemoryStream(imageB));

            Type dataType = a.GetType("ProjectA." + typeName, throwOnError: true)!;
            object value = Activator.CreateInstance(dataType)!;
            dataType.GetProperty("Id")!.SetValue(value, 7);
            dataType.GetProperty("Name")!.SetValue(value, "x");
            dataType.GetProperty("Hidden")!.SetValue(value, 5);

            MethodInfo toJson = b.GetType("ProjectB.Probe", throwOnError: true)!.GetMethod("ToJson")!;
            string json = (string)toJson.Invoke(null, new object[] { value })!;
            this.logger.WriteLine(json);
            return json;
        }
        finally
        {
            alc.Unload();
        }
    }
}
