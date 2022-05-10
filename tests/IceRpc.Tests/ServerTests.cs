// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
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

    /// <summary>Verifies that <see cref="Server.ShutdownComplete"/> task is completed after
    /// <see cref="Server.ShutdownAsync(CancellationToken)"/> completed.</summary>
    [Test]
    public async Task The_shutdown_complete_task_is_completed_after_shutdow()
    {
        await using var server = new Server(ConnectionOptions.DefaultDispatcher);

        await server.ShutdownAsync();

        Assert.That(server.ShutdownComplete.IsCompleted, Is.True);
    }
}
