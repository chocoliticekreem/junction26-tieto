// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class MetadataGenericStructDiagnosticsTests
{
    private const string LibSource = """
        using MessagePack;

        namespace Lib
        {
            [MessagePackObject]
            public struct Item<T>
            {
                [Key(0)]
                public string Key;

                public int Extra;
            }
        }
        """;

    private const string AppSource = """
        using MessagePack;
        using Lib;

        namespace App
        {
            [MessagePackObject]
            public class Cmd
            {
                [Key(0)]
                public Item<int> Member { get; set; }
            }
        }
        """;

    [Fact]
    public async Task ClosedGenericStructFromReferencedAssembly_WithUnkeyedPublicField_ReportsNothingInConsumer()
    {
        var framework = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = new List<MetadataReference>();
        references.AddRange(framework);
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        var libCompilation = CSharpCompilation.Create(
            "Lib",
            new[] { CSharpSyntaxTree.ParseText(LibSource) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emitResult = libCompilation.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        MetadataReference libReference = MetadataReference.CreateFromImage(stream.ToArray());

        var test = new VerifyCS.Test()
        {
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
        };
        test.TestState.Sources.Add(AppSource);
        test.TestState.AdditionalReferences.Add(libReference);

        await test.RunAsync();
    }
}
