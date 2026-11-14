// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack;

namespace Receipts.GeneratedTests;

public class ReceiptsUInt8RangeEncodingTests
{
    [Theory]
    [InlineData(128u)]
    [InlineData(200u)]
    [InlineData(255u)]
    public void UInt32_InUInt8Range_UsesUInt8Encoding(uint value)
    {
        Span<byte> buffer = stackalloc byte[16];
        Assert.True(MessagePackPrimitives.TryWrite(buffer, value, out int written));
        Assert.Equal(2, written);
        Assert.Equal((byte)0xcc, buffer[0]);
        Assert.Equal((byte)value, buffer[1]);
    }

    [Theory]
    [InlineData(128ul)]
    [InlineData(200ul)]
    [InlineData(255ul)]
    public void UInt64_InUInt8Range_UsesUInt8Encoding(ulong value)
    {
        Span<byte> buffer = stackalloc byte[16];
        Assert.True(MessagePackPrimitives.TryWrite(buffer, value, out int written));
        Assert.Equal(2, written);
        Assert.Equal((byte)0xcc, buffer[0]);
        Assert.Equal((byte)value, buffer[1]);
    }
}
