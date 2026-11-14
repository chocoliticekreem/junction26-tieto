// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack;

namespace Receipts.GeneratedTests;

public class DecimalRoundTripReceiptTests
{
    [Fact]
    public void DecimalRoundTripsWithoutPrecisionLoss()
    {
        decimal original = 1.2345m;
        byte[] bytes = MessagePackSerializer.Serialize(original);
        decimal roundTripped = MessagePackSerializer.Deserialize<decimal>(bytes);
        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void SmallDecimalRoundTripsWithoutPrecisionLoss()
    {
        decimal original = 0.001m;
        byte[] bytes = MessagePackSerializer.Serialize(original);
        decimal roundTripped = MessagePackSerializer.Deserialize<decimal>(bytes);
        Assert.Equal(original, roundTripped);
    }
}
