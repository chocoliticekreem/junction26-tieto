// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class GeneratedExcludeFromCodeCoverageTests
{
    [Fact]
    public async Task ExistingExcludeFromCodeCoverageOnResolverDoesNotDuplicateAttribute()
    {
        string source = """
            using MessagePack;

            [MessagePackObject]
            public class MyObject
            {
                [Key(0)]
                public int Value { get; set; }
            }

            [GeneratedMessagePackResolver]
            [global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
            public partial class TestResolver
            {
            }
            """;

        var test = new VerifyCS.Test
        {
            TestState = { Sources = { source } },
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck,
        };

        await test.RunAsync();
    }
}
