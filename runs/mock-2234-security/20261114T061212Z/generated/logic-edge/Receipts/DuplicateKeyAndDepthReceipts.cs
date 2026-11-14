// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;

namespace Receipts.GeneratedTests;

public class DuplicateKeyAndDepthReceipts
{
    [Fact]
    public void DepthStep_AtMaximumDepth_Throws()
    {
        MessagePackSecurity security = MessagePackSecurity.UntrustedData.WithMaximumObjectGraphDepth(2);
        var reader = new MessagePackReader(new ReadOnlyMemory<byte>(new byte[] { 0xc0 }));
        reader.Depth = 2;
        bool threw = false;
        try
        {
            security.DepthStep(ref reader);
        }
        catch (InsufficientExecutionStackException)
        {
            threw = true;
        }

        Assert.True(threw, "DepthStep must throw when the reader is already at MaximumObjectGraphDepth.");
    }

    [Fact]
    public async Task DuplicateIntKey_IsReportedOrBothMembersFormatted()
    {
        string source = """
            using MessagePack;

            namespace TestNs
            {
                [GeneratedMessagePackResolver]
                partial class MyResolver { }

                [MessagePackObject]
                public class Dup
                {
                    [Key(0)]
                    public int FirstMember { get; set; }

                    [Key(0)]
                    public int SecondMember { get; set; }
                }
            }
            """;

        var frameworkRefs = await ReferenceAssemblies.Net.Net80.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var references = frameworkRefs.Concat(ReferencesHelper.GetReferences(ReferencesSet.MessagePack)).ToList();
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp7_3));
        var compilation = CSharpCompilation.Create(
            "ReceiptsDupKeyAsm",
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new MessagePack.SourceGenerator.MessagePackGenerator().AsSourceGenerator());
        driver = driver.RunGenerators(compilation);
        var result = driver.GetRunResult();

        string generated = string.Join("\n", result.Results.SelectMany(r => r.GeneratedSources).Select(s => s.SourceText.ToString()));
        bool reported = result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)
            || result.Results.Any(r => r.Exception is not null || r.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));

        Assert.True(
            reported || generated.Contains("SecondMember"),
            "A duplicate [Key(0)] must either be reported or both members must be formatted; it must not be silently dropped.");
    }
}
