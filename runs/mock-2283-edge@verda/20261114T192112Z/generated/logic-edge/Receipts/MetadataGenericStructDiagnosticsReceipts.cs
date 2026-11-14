// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class MetadataGenericStructDiagnosticsReceipts
{
    [Fact]
    public async Task KeylessPropertyOnMetadataGenericStructDoesNotCrashGeneratorOrAnalyzers()
    {
        const string libSource = """
            using MessagePack;

            namespace Lib
            {
                [MessagePackObject]
                public struct Item<T>
                {
                    public T Value { get; set; }
                }
            }
            """;

        ImmutableArray<MetadataReference> frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        CSharpCompilation libCompilation = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(libSource, new CSharpParseOptions(LanguageVersion.Latest)) },
            frameworkReferences.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var libImage = new MemoryStream();
        EmitResult emitResult = libCompilation.Emit(libImage);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        var test = new VerifyCS.Test
        {
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
            TestState =
            {
                Sources =
                {
                    """
                    using MessagePack;
                    using Lib;

                    namespace App
                    {
                        [MessagePackObject]
                        public partial class Cmd
                        {
                            [Key(0)]
                            public string Scope { get; set; } = string.Empty;

                            [Key(1)]
                            public Item<int> Member { get; set; }
                        }
                    }
                    """,
                },
                AdditionalReferences =
                {
                    MetadataReference.CreateFromImage(libImage.ToArray()),
                },
            },
        };

        await test.RunAsync();
    }
}
