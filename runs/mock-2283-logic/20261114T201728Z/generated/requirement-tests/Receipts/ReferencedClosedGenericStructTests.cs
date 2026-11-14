// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class ReferencedClosedGenericStructTests(ITestOutputHelper logger)
{
    private const string AppSource = """
        using MessagePack;
        using Lib;

        namespace App
        {
            [MessagePackObject]
            public sealed partial record Cmd([property: Key(0)] string Scope)
            {
                [Key(1)]
                public Item<int> Member { get; init; }
            }

            [GeneratedMessagePackResolver]
            partial class AppResolver
            {
            }
        }
        """;

    private const string RecordStructLib = """
        using MessagePack;
        namespace Lib;

        [MessagePackObject]
        public partial record struct Item<T>([property: Key(0)] string Key);
        """;

    private const string StructLib = """
        using MessagePack;
        namespace Lib;

        [MessagePackObject]
        public partial struct Item<T>
        {
            [Key(0)]
            public string Key { get; set; }
        }
        """;

    [Fact]
    public async Task RecordStructFromReferencedAssembly_NoAnalyzerOrGeneratorFailure()
    {
        await this.RunVerifierAsync(RecordStructLib);
    }

    [Fact]
    public async Task StructFromReferencedAssembly_NoAnalyzerOrGeneratorFailure()
    {
        await this.RunVerifierAsync(StructLib);
    }

    [Fact]
    public async Task RecordStructFromReferencedAssembly_GeneratorProducesResolver()
    {
        List<MetadataReference> references = await GetReferencesAsync();
        MetadataReference lib = this.BuildLib(RecordStructLib, references);
        CSharpParseOptions parseOptions = new(LanguageVersion.Latest);
        CSharpCompilation app = CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(AppSource, parseOptions) },
            references.Append(lib),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new[] { new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator() },
            parseOptions: parseOptions);
        driver = driver.RunGenerators(app);
        GeneratorDriverRunResult result = driver.GetRunResult();
        foreach (Diagnostic d in result.Diagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        Assert.All(result.Results, r => Assert.Null(r.Exception));
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "CS8785");
        Assert.Contains(result.GeneratedTrees, t => t.GetText().ToString().Contains("AppResolver"));
    }

    private static async Task<List<MetadataReference>> GetReferencesAsync()
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        return Enumerable.Concat<MetadataReference>(framework, ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
    }

    private MetadataReference BuildLib(string libSource, List<MetadataReference> references)
    {
        CSharpCompilation lib = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(libSource, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using MemoryStream stream = new();
        var emitResult = lib.Emit(stream);
        foreach (Diagnostic d in emitResult.Diagnostics)
        {
            logger.WriteLine(d.ToString());
        }

        Assert.True(emitResult.Success, "lib failed to compile");
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private async Task RunVerifierAsync(string libSource)
    {
        List<MetadataReference> references = await GetReferencesAsync();
        MetadataReference lib = this.BuildLib(libSource, references);
        VerifyCS.Test test = new VerifyCS.Test()
        {
            LanguageVersion = LanguageVersion.Latest,
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.TestState.Sources.Add(AppSource);
        test.TestState.AdditionalReferences.Add(lib);
        await test.RunAsync();
    }
}
