// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;

namespace IceRpc.Tests;

public class ConnectionOptionsTests
{
    [Test]
    public void Connection_Options_ArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new ConnectionOptions { ShutdownTimeout = TimeSpan.Zero });
        Assert.Throws<ArgumentException>(() => new ConnectionOptions { ConnectTimeout = TimeSpan.Zero });
        Assert.Throws<ArgumentException>(() => new ConnectionOptions { IdleTimeout = TimeSpan.Zero });
    }
}
