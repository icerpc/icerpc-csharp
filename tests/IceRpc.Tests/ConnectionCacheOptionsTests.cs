// Copyright (c) ZeroC, Inc.

using NUnit.Framework;

namespace IceRpc.Tests;

public class ConnectionCacheOptionsTests
{
    [TestCase(-1)]
    [TestCase(int.MinValue)]
    public void MaxConnections_rejects_negative_values(int value)
    {
        ArgumentOutOfRangeException? exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new ConnectionCacheOptions { MaxConnections = value });
        Assert.That(exception!.ActualValue, Is.EqualTo(value));
    }
}
