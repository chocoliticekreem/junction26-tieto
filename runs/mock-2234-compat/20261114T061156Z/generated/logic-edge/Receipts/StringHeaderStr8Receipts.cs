// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack;

namespace Receipts.GeneratedTests;

public class StringHeaderStr8Receipts
{
    [Fact]
    public void StringHeader_100Bytes_UsesStr8()
    {
        byte[] buffer = new byte[8];
        bool ok = MessagePackPrimitives.TryWriteStringHeader(100u, buffer.AsSpan(), out int written);
        Assert.True(ok);
        Assert.Equal(2, written);
        Assert.Equal(MessagePackCode.Str8, buffer[0]);
        Assert.Equal((byte)100, buffer[1]);
    }

    [Fact]
    public void StringHeader_200Bytes_FitsTwoByteBuffer()
    {
        byte[] buffer = new byte[2];
        bool ok = MessagePackPrimitives.TryWriteStringHeader(200u, buffer.AsSpan(), out int written);
        Assert.True(ok);
        Assert.Equal(2, written);
        Assert.Equal(MessagePackCode.Str8, buffer[0]);
        Assert.Equal((byte)200, buffer[1]);
    }
}
