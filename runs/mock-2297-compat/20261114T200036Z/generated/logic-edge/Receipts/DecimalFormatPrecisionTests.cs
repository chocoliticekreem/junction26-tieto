// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;

namespace Receipts.GeneratedTests;

public class DecimalFormatPrecisionTests
{
    [Theory]
    [InlineData("1.2345")]
    [InlineData("0.001")]
    [InlineData("-12345.6789")]
    public void DecimalRoundTripsExactly(string text)
    {
        decimal value = decimal.Parse(text, CultureInfo.InvariantCulture);
        byte[] bytes = global::MessagePack.MessagePackSerializer.Serialize(value);
        decimal result = global::MessagePack.MessagePackSerializer.Deserialize<decimal>(bytes);
        Assert.Equal(value, result);
    }

    [Fact]
    public void DecimalPreservesScale()
    {
        decimal value = 1.5m;
        byte[] bytes = global::MessagePack.MessagePackSerializer.Serialize(value);
        decimal result = global::MessagePack.MessagePackSerializer.Deserialize<decimal>(bytes);
        Assert.Equal(value.ToString(CultureInfo.InvariantCulture), result.ToString(CultureInfo.InvariantCulture));
    }
}
