// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack;

namespace Receipts.GeneratedTests;

public class UntrustedDataDepthLimitReceipts
{
    [Fact]
    public void UntrustedData_RejectsGraphNestedDeeperThan500()
    {
        const int depth = 1000;
        byte[] bytes = new byte[depth + 1];
        for (int i = 0; i < depth; i++)
        {
            bytes[i] = 0x91; // fixarray with one element
        }

        bytes[depth] = 0xC0; // nil

        MessagePackSerializerOptions options = MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.UntrustedData);
        Exception? ex = Record.Exception(() => MessagePackSerializer.Deserialize<object>(bytes, options));
        Assert.NotNull(ex);
    }

    [Fact]
    public void UntrustedData_MaximumObjectGraphDepthIsAtMost500()
    {
        Assert.True(MessagePackSecurity.UntrustedData.MaximumObjectGraphDepth <= 500, $"UntrustedData depth limit was {MessagePackSecurity.UntrustedData.MaximumObjectGraphDepth}");
    }
}
