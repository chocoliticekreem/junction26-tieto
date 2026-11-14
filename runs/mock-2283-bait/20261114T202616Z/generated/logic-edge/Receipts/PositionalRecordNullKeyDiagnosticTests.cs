// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class PositionalRecordNullKeyDiagnosticTests
{
    [Fact]
    public async Task NullStringKeyOnPositionalRecordPropertyIsStillReported()
    {
        string testSource = """
            using MessagePack;

            [MessagePackObject]
            public partial record Cmd([property: Key((string)null)] string {|MsgPack005:Scope|});
            """;

        var test = new VerifyCS.Test
        {
            LanguageVersion = LanguageVersion.CSharp9,
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
        };
        test.TestState.Sources.Add(testSource);
        await test.RunAsync();
    }
}
