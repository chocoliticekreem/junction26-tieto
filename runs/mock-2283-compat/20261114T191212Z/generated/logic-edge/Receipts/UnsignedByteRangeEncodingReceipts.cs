// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack;

namespace Receipts.GeneratedTests;

public class UnsignedByteRangeEncodingReceipts
{
    [Fact]
    public void UnsignedValuesInByteRangeUseUInt8Encoding()
    {
        byte[] expected = new byte[] { 0xcc, 0xc8 };

        byte[] fromUInt = MessagePackSerializer.Serialize<uint>(200u);
        byte[] fromUShort = MessagePackSerializer.Serialize<ushort>((ushort)200);
        byte[] fromULong = MessagePackSerializer.Serialize<ulong>(200UL);

        Assert.Equal(expected, fromUInt);
        Assert.Equal(expected, fromUShort);
        Assert.Equal(expected, fromULong);
    }
}
