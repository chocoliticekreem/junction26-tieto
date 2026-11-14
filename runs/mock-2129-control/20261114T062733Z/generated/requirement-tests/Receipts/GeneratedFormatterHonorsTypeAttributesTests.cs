// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterHonorsTypeAttributesTests(ITestOutputHelper logger)
{
    private const string ProjectBSource = """
        using MessagePack;
        using MessagePack.Formatters;

        namespace ProjectB
        {
            [GeneratedMessagePackFormatter]
            partial class MyTypeFormatter : IMessagePackFormatter<ProjectA.MyType>;

            [GeneratedMessagePackResolver]
            partial class MyResolver
            {
            }
        }
        """;

    [Fact]
    public async Task DataContractMemberNamesAreHonored()
    {
        string sourceA = """
            using System.Runtime.Serialization;

            namespace ProjectA
            {
                [DataContract]
                public class MyType
                {
                    [DataMember(Name = "id")]
                    public int Id { get; set; }

                    [DataMember(Name = "name")]
                    public string Name { get; set; }

                    public int Extra { get; set; }
                }
            }
            """;

        (Assembly projectA, Assembly projectB) = await this.BuildAsync(sourceA, ProjectBSource);
        Type dataType = projectA.GetType("ProjectA.MyType", throwOnError: true)!;
        object value = Activator.CreateInstance(dataType)!;
        dataType.GetProperty("Id")!.SetValue(value, 7);
        dataType.GetProperty("Name")!.SetValue(value, "x");
        dataType.GetProperty("Extra")!.SetValue(value, 99);

        string json = SerializeToJson(projectB, dataType, value);
        logger.WriteLine(json);
        Assert.Contains("\"id\":7", json);
        Assert.Contains("\"name\":\"x\"", json);
        Assert.DoesNotContain("Extra", json);
        Assert.DoesNotContain("\"Id\"", json);
    }

    [Fact]
    public async Task IgnoreDataMemberIsHonoredInContractlessMode()
    {
        string sourceA = """
            using System.Runtime.Serialization;

            namespace ProjectA
            {
                public class MyType
                {
                    public int Id { get; set; }

                    [IgnoreDataMember]
                    public string Secret { get; set; }
                }
            }
            """;

        (Assembly projectA, Assembly projectB) = await this.BuildAsync(sourceA, ProjectBSource);
        Type dataType = projectA.GetType("ProjectA.MyType", throwOnError: true)!;
        object value = Activator.CreateInstance(dataType)!;
        dataType.GetProperty("Id")!.SetValue(value, 5);
        dataType.GetProperty("Secret")!.SetValue(value, "hidden");

        string json = SerializeToJson(projectB, dataType, value);
        logger.WriteLine(json);
        Assert.Contains("\"Id\":5", json);
        Assert.DoesNotContain("Secret", json);
        Assert.DoesNotContain("hidden", json);
    }

    private static string SerializeToJson(Assembly projectB, Type dataType, object value)
    {
        Type formatterType = projectB.GetType("ProjectB.MyTypeFormatter", throwOnError: true)!;
        object formatter = Activator.CreateInstance(formatterType)!;
        global::MessagePack.IFormatterResolver resolver = global::MessagePack.Resolvers.CompositeResolver.Create(
            new[] { (global::MessagePack.Formatters.IMessagePackFormatter)formatter },
            new global::MessagePack.IFormatterResolver[] { global::MessagePack.Resolvers.StandardResolver.Instance });
        global::MessagePack.MessagePackSerializerOptions options = global::MessagePack.MessagePackSerializerOptions.Standard.WithResolver(resolver);
        byte[] bytes = global::MessagePack.MessagePackSerializer.Serialize(dataType, value, options);
        return global::MessagePack.MessagePackSerializer.ConvertToJson(new ReadOnlyMemory<byte>(bytes));
    }

    private async Task<(Assembly ProjectA, Assembly ProjectB)> BuildAsync(string sourceA, string sourceB)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        CSharpCompilation compilationA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText(sourceA) },
            framework,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        byte[] imageA = Emit(compilationA);

        List<MetadataReference> referencesB = new(framework);
        referencesB.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        referencesB.Add(MetadataReference.CreateFromImage(imageA));
        CSharpParseOptions parseOptions = new(LanguageVersion.CSharp12);
        CSharpCompilation compilationB = CSharpCompilation.Create(
            "ProjectB",
            new[] { CSharpSyntaxTree.ParseText(sourceB, parseOptions) },
            referencesB,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilationB, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        foreach (SyntaxTree tree in driver.GetRunResult().GeneratedTrees)
        {
            logger.WriteLine(tree.FilePath);
            logger.WriteLine(tree.ToString());
        }

        Assert.DoesNotContain(generatorDiagnostics, d => d.Severity == DiagnosticSeverity.Error || d.Id == "CS8785");
        byte[] imageB = Emit(output);

        AssemblyLoadContext context = new("Receipts." + nameof(GeneratedFormatterHonorsTypeAttributesTests), isCollectible: true);
        Assembly loadedA = context.LoadFromStream(new MemoryStream(imageA));
        Assembly loadedB = context.LoadFromStream(new MemoryStream(imageB));
        return (loadedA, loadedB);
    }

    private static byte[] Emit(Compilation compilation)
    {
        using MemoryStream stream = new();
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return stream.ToArray();
    }
}
