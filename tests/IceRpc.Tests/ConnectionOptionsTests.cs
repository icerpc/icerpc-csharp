// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;

namespace IceRpc.Tests;

public class ConnectionOptionsTests
{
    [Test]
    public void Connection_Options_ArgumentException() =>
        Assert.Throws<ArgumentException>(() => new ConnectionOptions { Timeout = TimeSpan.Zero });
}
