// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using NUnit.Framework;

namespace IceRpc.Tests;

[Timeout(30000)]
public class ConnectionTests
{
    [Test]
    public void Connection_Options_ArgumentException()
    {
        Assert.Throws<ArgumentException>(() => new ConnectionOptions { CloseTimeout = TimeSpan.Zero });
        Assert.Throws<ArgumentException>(() => new ConnectionOptions { ConnectTimeout = TimeSpan.Zero });
    }
}
