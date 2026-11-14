// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack;

namespace Receipts.GeneratedTests;

public class SecurityDepthStepOffByOneTests
{
    [Fact]
    public void DepthStep_ThrowsWhenDepthEqualsMaximum()
    {
        MessagePackSecurity security = MessagePackSecurity.UntrustedData.WithMaximumObjectGraphDepth(1);
        var reader = new MessagePackReader(new ReadOnlyMemory<byte>(new byte[] { 0xc0 }));
        reader.Depth = 1;
        bool threw = false;
        try
        {
            security.DepthStep(ref reader);
        }
        catch (InsufficientExecutionStackException)
        {
            threw = true;
        }

        Assert.True(threw, "DepthStep must reject stepping beyond MaximumObjectGraphDepth (depth 1 with max 1).");
    }

    [Fact]
    public void DepthStep_ZeroMaximumRejectsAnyNesting()
    {
        MessagePackSecurity security = MessagePackSecurity.UntrustedData.WithMaximumObjectGraphDepth(0);
        var reader = new MessagePackReader(new ReadOnlyMemory<byte>(new byte[] { 0xc0 }));
        reader.Depth = 0;
        bool threw = false;
        try
        {
            security.DepthStep(ref reader);
        }
        catch (InsufficientExecutionStackException)
        {
            threw = true;
        }

        Assert.True(threw, "With MaximumObjectGraphDepth = 0 no nesting step may be allowed.");
    }
}
