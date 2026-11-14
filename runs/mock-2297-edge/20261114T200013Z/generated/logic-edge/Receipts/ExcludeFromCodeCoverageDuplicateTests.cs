// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageDuplicateTests
{
    [Fact]
    public async Task UserResolverAlreadyExcludedFromCoverage_CompilesWithoutDuplicateAttribute()
    {
        string testSource = """
            using System.Diagnostics.CodeAnalysis;
            using MessagePack;

            [MessagePackObject]
            public class MyObject
            {
                [Key(0)]
                public int Value { get; set; }
            }

            [ExcludeFromCodeCoverage]
            [GeneratedMessagePackResolver]
            partial class MyResolver
            {
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
