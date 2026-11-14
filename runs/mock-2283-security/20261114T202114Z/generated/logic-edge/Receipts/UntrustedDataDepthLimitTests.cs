// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using MessagePack;

namespace Receipts.GeneratedTests;

public class UntrustedDataDepthLimitTests
{
    [Fact]
    public void UntrustedData_MaximumObjectGraphDepth_Is500()
    {
        MessagePackSecurity.UntrustedData.MaximumObjectGraphDepth.Should().Be(500);
    }

    [Fact]
    public void UntrustedData_RejectsGraphDeeperThan500()
    {
        const int depth = 600;
        byte[] bytes = new byte[depth + 1];
        for (int i = 0; i < depth; i++)
        {
            bytes[i] = 0x91; // fixarray of length 1
        }

        bytes[depth] = 0xC0; // nil
        MessagePackSerializerOptions options = MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.UntrustedData);
        Action act = () => MessagePackSerializer.Deserialize<object>(new ReadOnlyMemory<byte>(bytes), options);
        act.Should().Throw<MessagePackSerializationException>();
    }
}
