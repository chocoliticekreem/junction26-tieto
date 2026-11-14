// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.CodeAnalysis.Testing;
using VerifyCS = CSharpSourceGeneratorVerifier<MessagePack.SourceGenerator.MessagePackGenerator>;

namespace Receipts.GeneratedTests;

public class ExcludeFromCodeCoverageDuplicateTests
{
    [Fact]
    public async Task UserAnnotatedGeneratedResolverStillCompiles()
    {
        string source = """
            using System.Diagnostics.CodeAnalysis;
            using MessagePack;

            namespace MyNs
            {
                [ExcludeFromCodeCoverage]
                [GeneratedMessagePackResolver]
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

        var test = new VerifyCS.Test();
        test.TestState.Sources.Add(source);
        test.TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck;
        await test.RunAsync();
    }

    [Fact]
    public async Task UserAnnotatedCompositeResolverStillCompiles()
    {
        string source = """
            using System.Diagnostics.CodeAnalysis;
            using MessagePack;
            using MessagePack.Resolvers;

            namespace MyNs
            {
                [ExcludeFromCodeCoverage]
                [CompositeResolver(typeof(StandardResolver))]
                partial class MyComposite
                {
                }
            }
            """;

        var test = new VerifyCS.Test();
        test.TestState.Sources.Add(source);
        test.TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck;
        await test.RunAsync();
    }
}
