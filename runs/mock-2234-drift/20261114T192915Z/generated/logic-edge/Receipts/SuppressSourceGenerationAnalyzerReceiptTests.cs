// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class SuppressSourceGenerationAnalyzerReceiptTests
{
    [Fact]
    public async Task MissingKey_WithSuppressSourceGeneration_StillReportsMsgPack004()
    {
        string testSource = """
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class MyObject
            {
                [Key(0)]
                public int A { get; set; }

                public int {|MsgPack004:B|} { get; set; }
            }
            """;

        var test = new VerifyCS.Test
        {
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
        };
        test.TestState.Sources.Add(testSource);
        await test.RunAsync();
    }
}
