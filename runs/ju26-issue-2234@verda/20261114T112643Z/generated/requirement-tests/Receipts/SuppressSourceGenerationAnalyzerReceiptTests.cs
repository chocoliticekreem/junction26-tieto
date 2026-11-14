// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class SuppressSourceGenerationAnalyzerReceiptTests
{
    [Fact]
    public async Task DuplicateIntKeysReportMsgPack005WhenSourceGenerationIsSuppressed()
    {
        const string source = """
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class MyObject
            {
                [Key(0)]
                public int First { get; set; }

                [Key(0)]
                public int Second { get; set; }
            }
            """;

        VerifyCS.Test test = CreateMsgPack005Test(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task DuplicateStringKeysReportMsgPack005WhenSourceGenerationIsSuppressed()
    {
        const string source = """
            using MessagePack;

            [MessagePackObject(SuppressSourceGeneration = true)]
            public class MyObject
            {
                [Key("same")]
                public string First { get; set; }

                [Key("same")]
                public string Second { get; set; }
            }
            """;

        VerifyCS.Test test = CreateMsgPack005Test(source);
        await test.RunAsync();
    }

    private static VerifyCS.Test CreateMsgPack005Test(string source)
    {
        var test = new VerifyCS.Test
        {
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
        };
        test.TestState.Sources.Add(source);
        test.TestState.ExpectedDiagnostics.Add(new DiagnosticResult("MsgPack005", DiagnosticSeverity.Error));
        return test;
    }
}
