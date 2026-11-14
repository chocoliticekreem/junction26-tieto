// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack;

namespace Receipts.GeneratedTests;

public class UInt32CompactEncodingReceiptTests
{
    [Theory]
    [InlineData(127, new byte[] { 0x7F })]
    [InlineData(128, new byte[] { 0xCC, 0x80 })]
    [InlineData(200, new byte[] { 0xCC, 0xC8 })]
    [InlineData(255, new byte[] { 0xCC, 0xFF })]
    public void UInt32UsesCompactFixIntAndUInt8Encoding(uint value, byte[] expected)
    {
        byte[] actual = MessagePackSerializer.Serialize(value);

        Assert.Equal(expected, actual);
    }
}
