// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Receipts.GeneratedTests;

public class MessagePackSecurityCloneTests
{
    [Fact]
    public void WithMaximumObjectGraphDepth_PreservesHashCollisionResistant()
    {
        Assert.True(MessagePack.MessagePackSecurity.UntrustedData.HashCollisionResistant);
        MessagePack.MessagePackSecurity modified = MessagePack.MessagePackSecurity.UntrustedData.WithMaximumObjectGraphDepth(100);
        Assert.Equal(100, modified.MaximumObjectGraphDepth);
        Assert.True(modified.HashCollisionResistant);
    }
}
