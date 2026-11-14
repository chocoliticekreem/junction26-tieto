// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Receipts.GeneratedTests;

public class ReceiptsUInt32WriterTests
{
    [Theory]
    [InlineData(128u)]
    [InlineData(200u)]
    [InlineData(255u)]
    public void UInt32InUInt8RangeUsesUInt8Encoding(uint value)
    {
        byte[] buffer = new byte[16];
        _ = MessagePack.MessagePackPrimitives.TryWrite(buffer, value, out int bytesWritten);
        Assert.Equal(2, bytesWritten);
        Assert.Equal(new byte[] { 0xcc, (byte)value }, buffer.AsSpan(0, bytesWritten).ToArray());
    }
}
