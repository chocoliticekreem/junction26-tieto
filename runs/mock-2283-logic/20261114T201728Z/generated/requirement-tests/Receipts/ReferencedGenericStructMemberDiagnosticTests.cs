// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class ReferencedGenericStructMemberDiagnosticTests(ITestOutputHelper logger)
{
    [Fact]
    public async Task UnkeyedPublicMemberOfReferencedGenericStruct_IsNotReportedInConsumer()
    {
        string libSource = """
            using MessagePack;
            namespace Lib;

            [MessagePackObject]
            public partial struct Item<T>
            {
                [Key(0)]
                public string Key { get; set; }

                public int Extra { get; set; }
            }
            """;

        string appSource = """
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
            }
            """;

        ImmutableArray<MetadataReference> framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        List<MetadataReference> references = Enumerable.Concat<MetadataReference>(framework, ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
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

        VerifyCS.Test test = new VerifyCS.Test()
        {
            LanguageVersion = LanguageVersion.Latest,
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.TestState.Sources.Add(appSource);
        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromImage(stream.ToArray()));
        await test.RunAsync();
    }
}
