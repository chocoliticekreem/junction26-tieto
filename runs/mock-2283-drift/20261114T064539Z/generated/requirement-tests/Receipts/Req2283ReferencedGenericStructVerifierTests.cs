// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class Req2283ReferencedGenericStructVerifierTests
{
    private const string AppSource = """
        using MessagePack;
        using Lib;

        namespace App
        {
            [MessagePackObject]
            public sealed partial record Cmd([property: Key(0)] string Scope)
            {
                [Key(1)] public Item<int> Member { get; init; }
            }
        }
        """;

    private const string LibRecordStructSource = """
        using MessagePack;

        namespace Lib
        {
            [MessagePackObject]
            public partial record struct Item<T>([property: Key(0)] string Key);
        }
        """;

    private const string LibStructSource = """
        using MessagePack;

        namespace Lib
        {
            [MessagePackObject]
            public struct Item<T>
            {
                [Key(0)] public string Key { get; set; }
            }
        }
        """;

    private readonly ITestOutputHelper logger;

    public Req2283ReferencedGenericStructVerifierTests(ITestOutputHelper logger)
    {
        this.logger = logger;
    }

    [Fact]
    public async Task ReferencedGenericRecordStructMember_NoGeneratorOrAnalyzerException()
    {
        await RunAsync(LibRecordStructSource);
    }

    [Fact]
    public async Task ReferencedGenericStructMember_NoGeneratorOrAnalyzerException()
    {
        await RunAsync(LibStructSource);
    }

    private async Task RunAsync(string libSource)
    {
        var test = new VerifyCS.Test
        {
            LanguageVersion = LanguageVersion.CSharp12,
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
        };

        MetadataReference libReference = await CompileLibAsync(libSource, test.ReferenceAssemblies);
        test.TestState.Sources.Add(AppSource);
        test.TestState.AdditionalReferences.Add(libReference);

        this.logger.WriteLine("Running generator and analyzers against App with Lib as a metadata reference.");
        await test.RunAsync();
    }

    private static async Task<MetadataReference> CompileLibAsync(string libSource, ReferenceAssemblies referenceAssemblies)
    {
        ImmutableArray<MetadataReference> framework = await referenceAssemblies.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        CSharpCompilation compilation = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(libSource, new CSharpParseOptions(LanguageVersion.CSharp12)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emitResult = compilation.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }
}
