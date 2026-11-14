// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class ClosedGenericMetadataRecordStructReceiptTests
{
    [Fact]
    public async Task ClosedGenericRecordStructFromReferencedAssembly()
    {
        const string librarySource = """
            using MessagePack;
            namespace Lib;

            [MessagePackObject]
            public partial record struct Item<T>([property: Key(0)] string Key);
            """;

        ImmutableArray<MetadataReference> references = (await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None))
            .AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        CSharpCompilation library = CSharpCompilation.Create(
            assemblyName: "Lib",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(librarySource, new CSharpParseOptions(LanguageVersion.Latest)) },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using MemoryStream peImage = new();
        var emitResult = library.Emit(peImage);
        emitResult.Success.Should().BeTrue(string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference libraryReference = MetadataReference.CreateFromImage(peImage.ToArray());

        var test = new VerifyCS.Test(ReferencesSet.MessagePack)
        {
            LanguageVersion = LanguageVersion.Latest,
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
        };
        test.TestState.ReferenceAssemblies = ReferenceAssemblies.Net.Net80;
        test.TestState.AdditionalReferences.Add(libraryReference);
        test.TestState.Sources.Add("""
            using MessagePack;
            using Lib;
            namespace App;

            [MessagePackObject]
            public sealed partial record Cmd([property: Key(0)] string Scope)
            {
                [Key(1)]
                public Item<int> Member { get; init; }
            }

            [GeneratedMessagePackResolver]
            public partial class TestResolver
            {
            }
            """);

        await test.RunAsync();
    }
}
