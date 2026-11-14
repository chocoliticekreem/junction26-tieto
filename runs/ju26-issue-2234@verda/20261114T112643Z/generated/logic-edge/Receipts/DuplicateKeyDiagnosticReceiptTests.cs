// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class DuplicateKeyDiagnosticReceiptTests
{
    [Fact]
    public async Task DuplicateIntegerKeyStillReportsMsgPack005()
    {
        const string source = """
            using MessagePack;

            [MessagePackObject]
            public partial class DuplicateKeyType
            {
                [Key(0)]
                public int A { get; set; }

                [Key(0)]
                public int B { get; set; }

                [Key(1)]
                public int C { get; set; }
            }
            """;

        ImmutableArray<MetadataReference> references = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        references = references.AddRange(ReferencesHelper.GetReferences(ReferencesSet.MessagePack));
        CSharpCompilation compilation = CSharpCompilation.Create(
            "DuplicateKeyDiagnosticReceipt",
            new[] { CSharpSyntaxTree.ParseText(source) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        driver.GetRunResult().Diagnostics.Should().Contain(diagnostic => diagnostic.Id == "MsgPack005");
    }
}
