// Copyright (c) ZeroC, Inc.

using NUnit.Framework;

namespace IceRpc.Tests;

public class ConnectionOptionsTests
{
    [Test]
    public void Connection_Options_ArgumentException() =>
        Assert.Throws<ArgumentException>(() => new ConnectionOptions { InactivityTimeout = TimeSpan.Zero });

    [Test]
    public void Max_icerpc_bidirectional_streams_cannot_be_zero() =>
        Assert.Throws<ArgumentException>(() => new ConnectionOptions { MaxIceRpcBidirectionalStreams = 0 });

    [Test]
    public void Max_icerpc_unidirectional_streams_cannot_be_zero() =>
        Assert.Throws<ArgumentException>(() => new ConnectionOptions { MaxIceRpcUnidirectionalStreams = 0 });
}
