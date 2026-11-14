// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class RecordPositionalPropertyNullKeyReceiptTests
{
    [Fact]
    public async Task RecordPositionalPropertyWithNullKeyStillReportsMsgPack006()
    {
        string source = """
            using MessagePack;

            [MessagePackObject]
            public partial record MyRecord([property: Key(null)] string {|MsgPack006:Value|});
            """;

        var test = new VerifyCS.Test(ReferencesSet.MessagePack)
        {
            LanguageVersion = Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest,
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
        };
        test.TestState.Sources.Add(source);

        await test.RunAsync();
    }
}
