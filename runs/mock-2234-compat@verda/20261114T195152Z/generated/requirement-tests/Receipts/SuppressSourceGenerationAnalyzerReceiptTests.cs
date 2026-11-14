// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class SuppressSourceGenerationAnalyzerReceiptTests
{
    [Fact]
    public async Task SuppressedTypeStillReportsMsgPack004()
    {
        const string source = """
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public partial class SuppressedContract
            {
                public int {|MsgPack004:Value|} { get; set; }
            }
            """;

        var test = new VerifyCS.Test();
        test.TestState.Sources.Add(source);

        // With no expected generated sources, this also verifies that formatter
        // generation remains suppressed while the analyzer diagnostic is reported.
        await test.RunAsync();
    }

    [Fact]
    public async Task SuppressedValidTypeProducesNoGeneratedSource()
    {
        const string source = """
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public partial class SuppressedContract
            {
                [Key(0)]
                public int Value { get; set; }
            }
            """;

        var test = new VerifyCS.Test();
        test.TestState.Sources.Add(source);
        await test.RunAsync();
    }

    [Theory]
    [InlineData("[MessagePackObject]")]
    [InlineData("[MessagePackObject(SuppressSourceGeneration = false)]")]
    public async Task UnsuppressedAnalyzerBehaviorIsUnchanged(string attribute)
    {
        string source = $$"""
            using MessagePack;

            {{attribute}}
            public partial class UnsuppressedContract
            {
                public int {|MsgPack004:Value|} { get; set; }
            }
            """;

        var test = new VerifyCS.Test();
        test.TestState.Sources.Add(source);
        test.TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck;
        await test.RunAsync();
    }
}
