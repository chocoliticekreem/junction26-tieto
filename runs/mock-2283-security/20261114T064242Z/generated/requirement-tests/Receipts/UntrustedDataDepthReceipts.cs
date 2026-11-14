// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack;

namespace Receipts.GeneratedTests;

public class UntrustedDataDepthReceipts
{
    [Fact]
    public void UntrustedData_MaximumObjectGraphDepth_IsUnchanged()
    {
        // REQ-2283 is a generator/analyzer crash fix; it does not ask to relax the untrusted-data depth limit.
        Assert.Equal(500, MessagePackSecurity.UntrustedData.MaximumObjectGraphDepth);
    }
}
