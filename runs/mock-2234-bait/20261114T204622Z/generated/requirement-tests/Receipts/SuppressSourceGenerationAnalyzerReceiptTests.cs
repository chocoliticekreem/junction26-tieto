// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class SuppressSourceGenerationAnalyzerReceiptTests
{
    [Fact]
    public async Task SuppressedType_WithDerivedKeyAttributes_AnalyzerDoesNotThrow()
    {
        string testSource = """
            using MessagePack;

            public class CompositeKeyAttribute : KeyAttribute
            {
                public CompositeKeyAttribute(string prefix, int index)
                    : base(prefix + index)
                {
                }
            }

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class MyObject
            {
                [CompositeKey("a", 1)]
                public int A { get; set; }

                [CompositeKey("a", 2)]
                public int B { get; set; }
            }
            """;

        var test = new VerifyCS.Test
        {
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
        };
        test.TestState.Sources.Add(testSource);
        await test.RunAsync();
    }

    [Fact]
    public async Task SuppressedType_WithDerivedKeyAttributes_StillReportsMissingKey()
    {
        string testSource = """
            using MessagePack;

            public class CompositeKeyAttribute : KeyAttribute
            {
                public CompositeKeyAttribute(string prefix, int index)
                    : base(prefix + index)
                {
                }
            }

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class MyObject
            {
                [CompositeKey("a", 1)]
                public int A { get; set; }

                [CompositeKey("a", 2)]
                public int B { get; set; }

                public int {|MsgPack004:C|} { get; set; }
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
