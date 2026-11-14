// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class Req2283MetadataDiagnosticsTests(ITestOutputHelper logger)
{
    [Fact]
    public async Task UnattributedMemberOfReferencedGenericStruct_NotReportedInConsumer()
    {
        string libSource = """
            using MessagePack;
            namespace Lib
            {
                [MessagePackObject]
                public struct Item<T>
                {
                    [Key(0)]
                    public string Key { get; set; }

                    public int Extra { get; set; }
                }
            }
            """;

        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        CSharpCompilation libCompilation = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(libSource, new CSharpParseOptions(LanguageVersion.Latest)) },
            framework.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var ms = new MemoryStream();
        var emit = libCompilation.Emit(ms);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        MetadataReference lib = MetadataReference.CreateFromImage(ms.ToArray());

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
        logger.WriteLine(appSource);

        var test = new VerifyCS.Test
        {
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
        };
        test.TestState.Sources.Add(appSource);
        test.TestState.AdditionalReferences.Add(lib);
        await test.RunAsync();
    }
}
