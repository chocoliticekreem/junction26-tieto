// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class SuppressSourceGenerationAnalyzerReceiptTests
{
    [Fact]
    public async Task SuppressedType_MemberWithoutKey_ReportsMsgPack004()
    {
        string testSource = """
using MessagePack;

[MessagePackObject(SuppressSourceGeneration = true)]
public class MyObject
{
    [Key(0)]
    public int A;

    public int {|MsgPack004:B|};
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
    public async Task SuppressedType_DuplicateKeys_ReportsMsgPack005WithoutAnalyzerCrash()
    {
        string testSource = """
using MessagePack;

[MessagePackObject(SuppressSourceGeneration = true)]
public class MyObject
{
    [Key(0)]
    public int A;

    [Key(0)]
    public int {|MsgPack005:B|};
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
