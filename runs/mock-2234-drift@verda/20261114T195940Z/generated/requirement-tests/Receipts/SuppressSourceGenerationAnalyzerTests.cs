// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class SuppressSourceGenerationAnalyzerTests
{
    [Fact]
    public async Task SuppressedType_MissingKey_ReportsMsgPack004()
    {
        const string source = """
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class MyObject
            {
                public int {|MsgPack004:Value|} { get; set; }
            }
            """;

        await RunAsync(source);
    }

    [Fact]
    public async Task SuppressedType_InaccessibleNestedType_ReportsMsgPack012()
    {
        const string source = """
            using MessagePack;

            class Outer
            {
                [MessagePackObject(SuppressSourceGeneration = true)]
                class {|MsgPack012:MyObject|}
                {
                    [Key(0)]
                    public int Value { get; set; }
                }
            }
            """;

        await RunAsync(source);
    }

    [Fact]
    public async Task UnsuppressedType_MissingKey_StillReportsMsgPack004()
    {
        const string source = """
            using MessagePack;

            [MessagePackObject]
            public partial class MyObject
            {
                public int {|MsgPack004:Value|} { get; set; }
            }
            """;

        await RunAsync(source);
    }

    private static async Task RunAsync(string source)
    {
        var test = new VerifyCS.Test();
        test.TestState.Sources.Add(source);
        test.TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck;
        await test.RunAsync();
    }
}
