// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack;

namespace Receipts.GeneratedTests;

public class UInt32EncodingReceiptTests
{
    [Theory]
    [InlineData(128u)]
    [InlineData(200u)]
    [InlineData(255u)]
    public void UInt32_InUInt8Range_IsEncodedAsUInt8(uint value)
    {
        byte[] bytes = MessagePackSerializer.Serialize<uint>(value);
        Assert.Equal(new byte[] { 0xcc, (byte)value }, bytes);
    }
}
