// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class SuppressSourceGenerationAnalyzerTests
{
    [Fact]
    public async Task SuppressedTypeStillReportsMsgPack004()
    {
        const string source = """
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class MyObject
            {
                public int {|MsgPack004:Value|} { get; set; }
            }
            """;

        var test = new VerifyCS.Test
        {
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
        };
        test.TestState.Sources.Add(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task UnsuppressedTypeStillReportsMsgPack004()
    {
        const string source = """
            using MessagePack;

            [MessagePackObject]
            public partial class MyObject
            {
                public int {|MsgPack004:Value|} { get; set; }
            }
            """;

        var test = new VerifyCS.Test
        {
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
        };
        test.TestState.Sources.Add(source);
        await test.RunAsync();
    }
}
