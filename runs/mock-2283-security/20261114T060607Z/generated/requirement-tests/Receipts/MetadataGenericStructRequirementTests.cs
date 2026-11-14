// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class MetadataGenericStructRequirementTests(ITestOutputHelper logger)
{
    private const string RecordStructLib = """
        using MessagePack;
        namespace Lib;

        [MessagePackObject]
        public partial record struct Item<T>([property: Key(0)] string Key);
        """;

    private const string PlainStructLib = """
        using MessagePack;
        namespace Lib;

        [MessagePackObject]
        public struct Item<T>
        {
            [Key(0)]
            public string Key { get; set; }
        }
        """;

    private static async Task<MetadataReference> BuildLibAsync(string source)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> refs = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var ms = new MemoryStream();
        var emit = lib.Emit(ms);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return MetadataReference.CreateFromImage(ms.ToArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ClosedGenericStructFromReferencedAssembly_NoAnalyzerOrGeneratorCrash(bool recordStruct)
    {
        MetadataReference libRef = await BuildLibAsync(recordStruct ? RecordStructLib : PlainStructLib);
        string appSource = """
            using MessagePack;
            using Lib;
            namespace App
            {
                [MessagePackObject]
                public sealed class Cmd
                {
                    [Key(0)]
                    public string Scope { get; set; }

                    [Key(1)]
                    public Item<int> Member { get; set; }
                }
            }
            """;
        var test = new VerifyCS.Test
        {
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
        };
        test.TestState.Sources.Add(appSource);
        test.TestState.AdditionalReferences.Add(libRef);
        await test.RunAsync();
    }

    [Fact]
    public async Task ClosedGenericRecordStructFromReferencedAssembly_ResolverIsGenerated()
    {
        MetadataReference libRef = await BuildLibAsync(RecordStructLib);
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> refs = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        refs.Add(libRef);
        string appSource = """
            using MessagePack;
            using Lib;
            namespace App
            {
                [MessagePackObject]
                public sealed class Cmd
                {
                    [Key(0)]
                    public string Scope { get; set; }

                    [Key(1)]
                    public Item<int> Member { get; set; }
                }

                [GeneratedMessagePackResolver]
                partial class MyResolver
                {
                }
            }
            """;
        CSharpParseOptions parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        CSharpCompilation app = CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(appSource, parseOptions) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(app, out Compilation output, out ImmutableArray<Diagnostic> diagnostics);
        GeneratorDriverRunResult result = driver.GetRunResult();
        foreach (Diagnostic d in diagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        Assert.All(result.Results, r => Assert.Null(r.Exception));
        Assert.DoesNotContain(diagnostics, d => d.Id == "CS8785");
        Assert.Contains(result.GeneratedTrees, t => t.FilePath.EndsWith("MyResolver.g.cs", StringComparison.Ordinal));
    }
}
