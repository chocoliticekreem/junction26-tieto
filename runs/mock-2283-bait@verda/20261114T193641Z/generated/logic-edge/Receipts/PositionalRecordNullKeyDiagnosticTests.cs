// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class PositionalRecordNullKeyDiagnosticTests
{
    [Fact]
    public async Task NullKeyOnPositionalRecordPropertyReportsMsgPack005()
    {
        var test = new VerifyCS.Test
        {
            LanguageVersion = LanguageVersion.Latest,
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
        };
        test.TestState.Sources.Add("""
            using MessagePack;

            [MessagePackObject]
            public partial record R([property: Key(null)] string {|MsgPack005:Value|});
            """);

        await test.RunAsync();
    }
}
