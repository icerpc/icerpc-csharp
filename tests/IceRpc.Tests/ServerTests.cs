// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Tests.Common;
using NUnit.Framework;

namespace IceRpc.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class ServerTests
{
    /// <summary>Verifies that using a DNS name in a server server address fails with <see cref="NotSupportedException"/>
    /// exception.</summary>
    [Test]
    public async Task DNS_name_cannot_be_used_in_a_server_server_address()
    {
        await using var server = new Server(ServiceNotFoundDispatcher.Instance, new Uri("icerpc://foo:10000"));

        Assert.Throws<NotSupportedException>(() => server.Listen());
    }

    /// <summary>Verifies that calling <see cref="Server.Listen"/> more than once fails with
    /// <see cref="InvalidOperationException"/> exception.</summary>
    [Test]
    public async Task Cannot_call_listen_twice()
    {
        await using var server = new Server(ServiceNotFoundDispatcher.Instance);
        server.Listen();

        Assert.Throws<InvalidOperationException>(() => server.Listen());
    }

    /// <summary>Verifies that calling <see cref="Server.Listen"/> on a disposed server fails with
    /// <see cref="InvalidOperationException"/>.</summary>
    [Test]
    public async Task Cannot_call_listen_on_a_disposed_server()
    {
        var server = new Server(ServiceNotFoundDispatcher.Instance);
        await server.DisposeAsync();

        Assert.Throws<InvalidOperationException>(() => server.Listen());
    }

    /// <summary>Verifies that <see cref="Server.ShutdownComplete"/> task is completed after
    /// <see cref="Server.ShutdownAsync(CancellationToken)"/> completed.</summary>
    [Test]
    public async Task The_shutdown_complete_task_is_completed_after_shutdown()
    {
        await using var server = new Server(ServiceNotFoundDispatcher.Instance);

        await server.ShutdownAsync();

        Assert.That(server.ShutdownComplete.IsCompleted, Is.True);
    }

    /// <summary>Verifies that Server.ServerAddress.Transport property is set.</summary>
    [Test]
    public async Task Server_server_address_transport_property_is_set([Values("ice", "icerpc")] string protocol)
    {
        // Arrange/Act
        await using var server = new Server(
            ServiceNotFoundDispatcher.Instance,
            new ServerAddress(Protocol.FromString(protocol)));

        // Assert
        Assert.That(server.ServerAddress.Transport, Is.Not.Null);
    }
}
