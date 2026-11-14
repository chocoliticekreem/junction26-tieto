// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using MessagePack;

namespace Receipts.GeneratedTests;

public class UntrustedDataComparerGuardTests
{
    [Fact]
    public void UntrustedDataRejectsUnknownKeyType()
    {
        Assert.Throws<TypeAccessException>(() => MessagePackSecurity.UntrustedData.GetEqualityComparer<CustomKey>());
    }

    public sealed class CustomKey
    {
        public int Value { get; set; }
    }
}
