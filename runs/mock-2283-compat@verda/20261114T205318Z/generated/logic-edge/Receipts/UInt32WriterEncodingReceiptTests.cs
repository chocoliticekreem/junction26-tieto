// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Buffers;
using FluentAssertions;
using MessagePack;

namespace Receipts.GeneratedTests;

public class UInt32WriterEncodingReceiptTests
{
    [Fact]
    public void ByteSizedUInt32UsesUInt8Encoding()
    {
        var output = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(output);
        writer.Write(200u);
        writer.Flush();

        output.WrittenSpan.ToArray().Should().Equal(new byte[] { 0xcc, 0xc8 });
    }
}
