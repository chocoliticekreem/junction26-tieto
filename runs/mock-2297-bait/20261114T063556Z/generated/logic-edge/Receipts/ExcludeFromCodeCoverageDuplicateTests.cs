// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageDuplicateTests
{
    [Fact]
    public async Task UserResolverAlreadyExcludedFromCoverage_StillCompiles()
    {
        string testSource = """
            using MessagePack;

            namespace MyNs
            {
                [GeneratedMessagePackResolver]
                [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
                partial class MyResolver
                {
                }

                [MessagePackObject]
                public class MyObject
                {
                    [Key(0)]
                    public int Value { get; set; }
                }
            }
            """;

        var test = new VerifyCS.Test()
        {
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
        };
        test.TestState.Sources.Add(testSource);
        await test.RunAsync();
    }
}
