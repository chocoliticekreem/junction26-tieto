// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack;

namespace Receipts.GeneratedTests;

public class UntrustedDataDepthLimitTests
{
    [Fact]
    public void UntrustedData_MaximumObjectGraphDepth_Is500()
    {
        Assert.Equal(500, MessagePackSecurity.UntrustedData.MaximumObjectGraphDepth);
    }
}
