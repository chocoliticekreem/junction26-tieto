// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public sealed class GeneratedFormatterEdgeCaseReceiptTests
{
    [Fact]
    public async Task MetadataTypeIsSupported()
    {
        var frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        CSharpCompilation projectA = CSharpCompilation.Create(
            "ProjectA",
            new[] { CSharpSyntaxTree.ParseText("namespace ProjectA { public class MyObject { public int Value { get; set; } } }") },
            frameworkReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using MemoryStream image = new();
        EmitResult emitResult = projectA.Emit(image);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        var test = new VerifyCS.Test
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
        };
        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromImage(image.ToArray()));
        test.TestState.Sources.Add("""
            using MessagePack;
            using MessagePack.Formatters;
            using ProjectA;

            [GeneratedMessagePackFormatter]
            public partial class MyObjectFormatter : IMessagePackFormatter<MyObject>
            {
            }
            """);

        await test.RunAsync();
    }

    [Fact]
    public async Task InheritedExplicitCallbacksCastThroughInterface()
    {
        var test = new VerifyCS.Test
        {
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
        };
        test.TestState.Sources.Add("""
            using MessagePack;
            using MessagePack.Formatters;

            public class CallbackBase : IMessagePackSerializationCallbackReceiver
            {
                void IMessagePackSerializationCallbackReceiver.OnBeforeSerialize()
                {
                }

                void IMessagePackSerializationCallbackReceiver.OnAfterDeserialize()
                {
                }
            }

            public class MyObject : CallbackBase
            {
                public int Value { get; set; }
            }

            [GeneratedMessagePackFormatter]
            public partial class MyObjectFormatter : IMessagePackFormatter<MyObject>
            {
            }
            """);

        await test.RunAsync();
    }
}
