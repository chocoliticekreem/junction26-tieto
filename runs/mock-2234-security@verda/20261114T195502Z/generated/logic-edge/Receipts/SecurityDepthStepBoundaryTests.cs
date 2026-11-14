// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack;

namespace Receipts.GeneratedTests;

public class SecurityDepthStepBoundaryTests
{
    [Fact]
    public void DepthStepRejectsDepthAtConfiguredMaximum()
    {
        MessagePackSecurity security = MessagePackSecurity.TrustedData.WithMaximumObjectGraphDepth(1);
        var reader = new MessagePackReader(new ReadOnlyMemory<byte>(new byte[] { 0x90 }));

        security.DepthStep(ref reader);

        bool threw = false;
        try
        {
            security.DepthStep(ref reader);
        }
        catch (InsufficientExecutionStackException)
        {
            threw = true;
        }

        Assert.True(threw, "A second depth step must be rejected when MaximumObjectGraphDepth is 1.");
    }
}
