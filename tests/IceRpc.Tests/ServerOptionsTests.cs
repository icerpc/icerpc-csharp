// Copyright (c) ZeroC, Inc.

using NUnit.Framework;

namespace IceRpc.Tests;

public class ServerOptionsTests
{
    [TestCase(-1)]
    [TestCase(int.MinValue)]
    public void MaxConnections_rejects_negative_values(int value)
    {
        ArgumentOutOfRangeException? exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new ServerOptions { MaxConnections = value });
        Assert.That(exception!.ActualValue, Is.EqualTo(value));
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(int.MinValue)]
    public void MaxPendingConnections_rejects_non_positive_values(int value)
    {
        ArgumentOutOfRangeException? exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new ServerOptions { MaxPendingConnections = value });
        Assert.That(exception!.ActualValue, Is.EqualTo(value));
    }
}
