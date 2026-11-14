// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using FluentAssertions;
using MessagePack;

namespace Receipts.GeneratedTests;

public class StringHeaderStr8Receipts
{
    [Fact]
    public void StringOf100Bytes_UsesStr8Header()
    {
        byte[] buffer = new byte[8];
        bool ok = MessagePackPrimitives.TryWriteStringHeader(buffer, 100, out int written);
        ok.Should().BeTrue();
        written.Should().Be(2);
        buffer[0].Should().Be(MessagePackCode.Str8);
        buffer[1].Should().Be((byte)100);
    }
}
