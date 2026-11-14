// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class MetadataGenericStructDiagnosticsTests
{
    [Fact]
    public async Task MetadataGenericStructWithNullKeyDoesNotCrashAnalyzer()
    {
        const string libSource = """
            using MessagePack;

            namespace Lib;

            [MessagePackObject]
            public partial struct Item<T>
            {
                [Key(null)]
                public string Value { get; set; } = "";
            }
            """;

        const string appSource = """
            using MessagePack;
            using Lib;

            namespace App;

            [MessagePackObject]
            public partial class Cmd
            {
                [Key(0)]
                public Item<int> Member { get; set; }
            }
            """;

        var references = new List<MetadataReference>(
            await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None));
        references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));

        var libCompilation = CSharpCompilation.Create(
            assemblyName: "Lib",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(libSource, new CSharpParseOptions(LanguageVersion.Latest)) },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var libImage = new MemoryStream();
        var emitResult = libCompilation.Emit(libImage);
        Assert.True(emitResult.Success, string.Join(Environment.NewLine, emitResult.Diagnostics));

        var test = new VerifyCS.Test
        {
            LanguageVersion = LanguageVersion.Latest,
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
        };
        test.TestState.Sources.Add(appSource);
        test.TestState.AdditionalReferences.Add(MetadataReference.CreateFromImage(libImage.ToArray()));

        await test.RunAsync();
    }
}
