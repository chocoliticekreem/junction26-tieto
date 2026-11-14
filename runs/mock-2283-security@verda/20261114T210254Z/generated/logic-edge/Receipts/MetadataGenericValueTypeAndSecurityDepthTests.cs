// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Buffers;
using FluentAssertions;
using MessagePack;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class MetadataGenericValueTypeAndSecurityDepthTests
{
    [Fact]
    public async Task ClosedGenericStructFromMetadataDoesNotCrashGeneratorOrAnalyzer()
    {
        const string libSource = """
            using MessagePack;
            namespace Lib;

            [MessagePackObject]
            public partial record struct Item<T>([property: Key(0)] string Key);
            """;

        const string appSource = """
            using MessagePack;
            using Lib;
            namespace App;

            [MessagePackObject]
            public sealed partial record Cmd([property: Key(0)] string Scope)
            {
                [Key(1)]
                public Item<int> Member { get; init; }
            }
            """;

        var references = new List<MetadataReference>();
        references.AddRange(await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None));
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        var libCompilation = CSharpCompilation.Create(
            "ReceiptsMetadataLib",
            new[] { CSharpSyntaxTree.ParseText(libSource, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        using var peImage = new MemoryStream();
        var emitResult = libCompilation.Emit(peImage);
        emitResult.Success.Should().BeTrue(string.Join(Environment.NewLine, emitResult.Diagnostics));

        var test = new VerifyCS.Test(ReferencesSet.MessagePack)
        {
            LanguageVersion = LanguageVersion.Latest,
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
        };
        test.TestState.Sources.Add(appSource);
        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromImage(peImage.ToArray()));

        await test.RunAsync();
    }

    [Fact]
    public void UntrustedDataStillRejectsGraphDeeperThan500()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        for (var i = 0; i < 501; i++)
        {
            writer.WriteArrayHeader(1);
        }

        writer.WriteNil();
        writer.Flush();

        var options = MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.UntrustedData);
        Action deserialize = () => MessagePackSerializer.Deserialize<object>(buffer.WrittenMemory, options);

        deserialize.Should().Throw<MessagePackSerializationException>();
    }
}
