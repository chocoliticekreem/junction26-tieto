// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class MetadataGenericStructDiagnosticsTests
{
    private const string LibrarySource = """
        using MessagePack;

        namespace Lib;

        [MessagePackObject]
        public partial struct Item<T>
        {
            public int Value { get; set; }
        }
        """;

    private const string AppSource = """
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
        public partial class AppResolver
        {
        }
        """;

    [Fact]
    public async Task UnkeyedMetadataGenericStructDoesNotReportDiagnosticInConsumer()
    {
        MetadataReference libraryReference = await CompileLibraryAsync();
        var test = new VerifyCS.Test(ReferencesSet.MessagePack)
        {
            LanguageVersion = LanguageVersion.Latest,
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
            TestState =
            {
                Sources = { AppSource },
                AdditionalReferences = { libraryReference },
            },
        };

        await test.RunAsync();
    }

    private static async Task<MetadataReference> CompileLibraryAsync()
    {
        ImmutableArray<MetadataReference> frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        ImmutableArray<MetadataReference> references = frameworkReferences.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        CSharpCompilation library = CSharpCompilation.Create(
            assemblyName: "Lib",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(LibrarySource, new CSharpParseOptions(LanguageVersion.Latest)) },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var stream = new MemoryStream();
        var emitResult = library.Emit(stream);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }
}
