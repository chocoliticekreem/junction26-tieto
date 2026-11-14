// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack;

namespace Receipts.GeneratedTests;

public class DecimalFixedPointFormattingReceiptTests
{
    [Fact]
    public void DecimalSerializationPreservesFractionalValue()
    {
        const decimal original = 123.456m;

        byte[] bytes = MessagePackSerializer.Serialize(original);
        decimal actual = MessagePackSerializer.Deserialize<decimal>(bytes);

        Assert.Equal(original, actual);
    }
}
