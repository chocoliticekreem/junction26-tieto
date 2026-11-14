// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using MessagePack;

namespace Receipts.GeneratedTests;

public class StringHeaderEncodingReceiptTests
{
    [Theory]
    [InlineData(32)]
    [InlineData(255)]
    public void StringsUpTo255BytesUseStr8Header(int length)
    {
        byte[] serialized = MessagePackSerializer.Serialize(new string('a', length));

        serialized.Should().HaveCount(length + 2);
        serialized[0].Should().Be(MessagePackCode.Str8);
        serialized[1].Should().Be((byte)length);
    }
}
