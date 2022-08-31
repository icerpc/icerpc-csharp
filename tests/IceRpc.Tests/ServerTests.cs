// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Transports;
using NUnit.Framework;

namespace IceRpc.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class ServerTests
{
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
    /// <see cref="ObjectDisposedException"/>.</summary>
    [Test]
    public async Task Cannot_call_listen_on_a_disposed_server()
    {
        var server = new Server(ServiceNotFoundDispatcher.Instance);
        await server.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => server.Listen());
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
            new ServerAddress(Protocol.Parse(protocol)));

        // Assert
        Assert.That(server.ServerAddress.Transport, Is.Not.Null);
    }

    /// <summary>Verifies that a the server's maximum connections setting is enforced.</summary>
    [Test]
    public async Task Max_connections_is_enforced()
    {
        // Arrange
        const int maxAccepted = 25;
        const int totalConnections = (maxAccepted * 2) - 1; // keep under maxAccepted * 2 so we can close in order
        using var waitSemaphore = new SemaphoreSlim(0);

        var dispatcher = new InlineDispatcher((request, cancellationToken) =>
        {
            waitSemaphore.Release();
            return new(new OutgoingResponse(request));
        });
        var colocTransport = new ColocTransport();
        var serverAddress = new ServerAddress(new Uri($"icerpc://server"));
        var clientConnectionOptions = new ClientConnectionOptions
        {
            ServerAddress = serverAddress,
        };

        await using var server = new Server(
                    new ServerOptions
                    {
                        MaxConnections = maxAccepted,
                        ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
                        ServerAddress = serverAddress,
                    },
                    multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        server.Listen();

        // Act
        var connections = new List<ClientConnection>();
        for (int i = 0; i < maxAccepted; i++)
        {
            var connection = new ClientConnection(
                clientConnectionOptions,
                multiplexedClientTransport: new SlicClientTransport(colocTransport.ClientTransport));
            connections.Add(connection);

            await new ServiceProxy(connection).IcePingAsync();
        }

        for (int i = 0; i < maxAccepted; i++)
        {
            await waitSemaphore.WaitAsync();
        }

        // Assert
        Assert.That(waitSemaphore.CurrentCount, Is.EqualTo(0));

        for (int i = maxAccepted; i < totalConnections; i++)
        {
            var connection = new ClientConnection(
                clientConnectionOptions,
                multiplexedClientTransport: new SlicClientTransport(colocTransport.ClientTransport));
            connections.Add(connection);
            _ = new ServiceProxy(connection).IcePingAsync();
        }

        Assert.That(waitSemaphore.CurrentCount, Is.EqualTo(0));

        for (int i = 0; i < totalConnections - maxAccepted; i++)
        {
            // Close a connection
            await connections[i].DisposeAsync();

            // Wait for the next connection's request to reach the dispatcher and release waitSemaphore
            await waitSemaphore.WaitAsync();

            // Check that the server is enforcing the max connections setting.
            // No extra connections have released waitSemaphore.
            Assert.That(waitSemaphore.CurrentCount, Is.EqualTo(0));
        }

        // Dispose all the connections
        connections.ForEach(async c => await c.DisposeAsync());
    }
}
