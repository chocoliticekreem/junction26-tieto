// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class DuplicateKeyDiagnosticsReceiptTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicateKeysAreReported(bool useStringKeys)
    {
        string key = useStringKeys ? "\"same\"" : "0";
        string source = $$"""
            using MessagePack;

            [MessagePackObject]
            public class DuplicateKeyObject
            {
                [Key({{key}})]
                public int First { get; set; }

                [Key({{key}})]
                public int Second { get; set; }
            }

            [GeneratedMessagePackResolver]
            public partial class TestResolver
            {
            }
            """;

        var frameworkReferences = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = frameworkReferences
            .Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            assemblyName: "DuplicateKeyDiagnosticsReceipt",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source) },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator());
        driver = driver.RunGenerators(compilation);

        GeneratorDriverRunResult result = driver.GetRunResult();
        result.Diagnostics.Should().Contain(
            diagnostic => diagnostic.Id == "MsgPack007",
            "duplicate MessagePack keys must still be diagnosed rather than silently discarded");
    }
}
