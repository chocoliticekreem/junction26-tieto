// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class GeneratedFormatterForReferencedTypeTests(ITestOutputHelper logger)
{
    private const string ProjectASource = """
        namespace ProjectA
        {
            public class MyType
            {
                public int Id { get; set; }

                public string Name { get; set; }
            }
        }
        """;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ContractlessFormatterIsGeneratedInConsumingAssembly(bool bodyless)
    {
        string body = bodyless ? ";" : "\n{\n}";
        string projectBSource = $$"""
            using MessagePack;
            using MessagePack.Formatters;

            namespace ProjectB
            {
                [GeneratedMessagePackFormatter]
                partial class MyTypeFormatter : IMessagePackFormatter<ProjectA.MyType>{{body}}

                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }
            }
            """;

        (Assembly projectA, Assembly projectB) = await this.BuildAsync(ProjectASource, projectBSource);
        Type dataType = projectA.GetType("ProjectA.MyType", throwOnError: true)!;
        object value = Activator.CreateInstance(dataType)!;
        dataType.GetProperty("Id")!.SetValue(value, 42);
        dataType.GetProperty("Name")!.SetValue(value, "hello");

        global::MessagePack.MessagePackSerializerOptions options = CreateOptions(projectB);
        byte[] bytes = global::MessagePack.MessagePackSerializer.Serialize(dataType, value, options);
        string json = global::MessagePack.MessagePackSerializer.ConvertToJson(new ReadOnlyMemory<byte>(bytes));
        logger.WriteLine(json);
        Assert.Contains("\"Id\":42", json);
        Assert.Contains("\"Name\":\"hello\"", json);

        object? roundTripped = global::MessagePack.MessagePackSerializer.Deserialize(dataType, new ReadOnlyMemory<byte>(bytes), options);
        Assert.NotNull(roundTripped);
        Assert.Equal((object)42, dataType.GetProperty("Id")!.GetValue(roundTripped));
        Assert.Equal((object)"hello", dataType.GetProperty("Name")!.GetValue(roundTripped));
    }

    private static global::MessagePack.MessagePackSerializerOptions CreateOptions(Assembly projectB)
    {
        Type formatterType = projectB.GetType("ProjectB.MyTypeFormatter", throwOnError: true)!;
        object formatter = Activator.CreateInstance(formatterType)!;
        global::MessagePack.IFormatterResolver resolver = global::MessagePack.Resolvers.CompositeResolver.Create(
            new[] { (global::MessagePack.Formatters.IMessagePackFormatter)formatter },
            new global::MessagePack.IFormatterResolver[] { global::MessagePack.Resolvers.StandardResolver.Instance });
        return global::MessagePack.MessagePackSerializerOptions.Standard.WithResolver(resolver);
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

        AssemblyLoadContext context = new("Receipts." + nameof(GeneratedFormatterForReferencedTypeTests), isCollectible: true);
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
