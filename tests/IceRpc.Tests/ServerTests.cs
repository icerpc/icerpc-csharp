// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports;
using NUnit.Framework;

namespace IceRpc.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class ServerTests
{
    /// <summary>Verifies that using a DNS name in a server endpoint fails with <see cref="NotSupportedException"/>
    /// exception.</summary>
    [Test]
    public async Task DNS_name_cannot_be_used_in_a_server_endpoint()
    {
        await using var server = new Server(ConnectionOptions.DefaultDispatcher, "icerpc://foo:10000");

        Assert.Throws<NotSupportedException>(() => server.Listen());
    }


    /// <summary>Verifies that calling <see cref="Server.Listen"/> more than once fails with
    /// <see cref="InvalidOperationException"/> exception.</summary>
    [Test]
    public async Task Cannot_call_listen_twice()
    {
        await using var server = new Server(ConnectionOptions.DefaultDispatcher);
        server.Listen();

        Assert.Throws<InvalidOperationException>(() => server.Listen());
    }

    /// <summary>Verifies that calling <see cref="Server.Listen"/> on a disposed server fails with
    /// <see cref="ObjectDisposedException"/>.</summary>
    [Test]
    public async Task Cannot_call_listen_on_a_disposed_server()
    {
        var server = new Server(ConnectionOptions.DefaultDispatcher);
        await server.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => server.Listen());
    }

    /// <summary>Verifies that two servers cannot listen on the same endpoint. The second attempt throws a
    /// <see cref="TransportException"/>.</summary>
    [Test]
    public async Task Two_servers_listening_on_the_same_endpoint_fails_with_transport_exception()
    {
        await using var server1 = new Server(ConnectionOptions.DefaultDispatcher, "icerpc://127.0.0.1:15001");
        await using var server2 = new Server(ConnectionOptions.DefaultDispatcher, "icerpc://127.0.0.1:15001");
        server1.Listen();

        Assert.Throws<TransportException>(() => server2.Listen());
    }
}
