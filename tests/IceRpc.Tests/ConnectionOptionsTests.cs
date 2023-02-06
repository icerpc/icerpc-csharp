// Copyright (c) ZeroC, Inc.

using NUnit.Framework;

namespace IceRpc.Tests;

public class ConnectionOptionsTests
{
    [Test]
    public void Connection_Options_ArgumentException() =>
        Assert.Throws<ArgumentException>(() => new ConnectionOptions { InactivityTimeout = TimeSpan.Zero });
}
