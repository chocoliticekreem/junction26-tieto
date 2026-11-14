// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack;

namespace Receipts.GeneratedTests;

public class MessagePackSecurityCopyReceipts
{
    [Fact]
    public void WithMaximumObjectGraphDepth_PreservesHashCollisionResistance()
    {
        Assert.True(MessagePackSecurity.UntrustedData.HashCollisionResistant);
        MessagePackSecurity derived = MessagePackSecurity.UntrustedData.WithMaximumObjectGraphDepth(123);
        Assert.Equal(123, derived.MaximumObjectGraphDepth);
        Assert.True(derived.HashCollisionResistant);
    }
}
