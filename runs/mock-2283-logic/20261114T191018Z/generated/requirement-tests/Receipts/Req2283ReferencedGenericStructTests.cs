// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class Req2283ReferencedGenericStructTests(ITestOutputHelper logger)
{
    private const string RecordStructLib = """
        using MessagePack;
        namespace Lib
        {
            [MessagePackObject]
            public partial record struct Item<T>([property: Key(0)] string Key);
        }
        """;

    private const string StructLib = """
        using MessagePack;
        namespace Lib
        {
            [MessagePackObject]
            public struct Item<T>
            {
                [Key(0)]
                public string Key { get; set; }
            }
        }
        """;

    private const string AppSource = """
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

    [Fact]
    public async Task ClosedGenericRecordStructFromReferencedAssembly_NoCrash()
    {
        MetadataReference lib = await CompileLibAsync(RecordStructLib);
        await RunAppAsync(lib);
    }

    [Fact]
    public async Task ClosedGenericStructFromReferencedAssembly_NoCrash()
    {
        MetadataReference lib = await CompileLibAsync(StructLib);
        await RunAppAsync(lib);
    }

    [Fact]
    public async Task ClosedGenericRecordStructFromReferencedAssembly_ResolverIsGenerated()
    {
        MetadataReference lib = await CompileLibAsync(RecordStructLib);
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        string app = AppSource + """

            namespace App
            {
                [GeneratedMessagePackResolver]
                partial class AppResolver { }
            }
            """;
        CSharpCompilation compilation = CSharpCompilation.Create(
            "App",
            new[] { CSharpSyntaxTree.ParseText(app, new CSharpParseOptions(LanguageVersion.Latest)) },
            framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).Append(lib),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator());
        driver = driver.RunGenerators(compilation);
        GeneratorDriverRunResult result = driver.GetRunResult();

        foreach (GeneratorRunResult r in result.Results)
        {
            Assert.True(r.Exception is null, r.Exception?.ToString());
        }

        Assert.Contains(result.Results.SelectMany(r => r.GeneratedSources), s => s.HintName.Contains("AppResolver"));
    }

    private static async Task<MetadataReference> CompileLibAsync(string source)
    {
        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return MetadataReference.CreateFromImage(ms.ToArray());
    }

    private async Task RunAppAsync(MetadataReference lib)
    {
        logger.WriteLine(AppSource);
        var test = new VerifyCS.Test
        {
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
        };
        test.TestState.Sources.Add(AppSource);
        test.TestState.AdditionalReferences.Add(lib);
        await test.RunAsync();
    }
}
