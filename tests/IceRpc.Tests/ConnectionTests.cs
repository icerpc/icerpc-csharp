// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports;
using NUnit.Framework;

namespace IceRpc.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class ConnectionTests
{
    /// <summary>Verifies that concurrent dispatches on a given connection are limited to max_dispatches.</summary>
    [TestCase("ice://colochost", 1)]
    [TestCase("ice://colochost", 10)]
    public async Task Connection_dispatches_requests_concurrently_up_to_max_dispatches(
        string endpoint,
        int maxDispatchesPerConnection)
    {
        // Arrange
        var colocTransport = new ColocTransport();
        var serverOptions = CreateServerOptions(endpoint, colocTransport);
        serverOptions.MaxDispatchesPerConnection = maxDispatchesPerConnection;
        using var startSemaphore = new SemaphoreSlim(maxDispatchesPerConnection);
        using var workSemaphore = new SemaphoreSlim(0);
        int count = 0;
        int maxCount = 0;
        var mutex = new object();

        serverOptions.Dispatcher = new InlineDispatcher(async (request, cancel) =>
        {
            await request.Payload.CompleteAsync(); // done with payload
            startSemaphore.Release();
            IncrementCount();
            await workSemaphore.WaitAsync(cancel);
            DecrementCount();
            return new OutgoingResponse(request);

            void DecrementCount()
            {
                lock (mutex)
                {
                    count--;
                }
            }

            void IncrementCount()
            {
                lock (mutex)
                {
                    count++;
                    maxCount = Math.Max(maxCount, count);
                }
            }
        });

        await using var server = new Server(serverOptions);
        server.Listen();

        await using var connection = new Connection(CreateConnectionOptions(server.Endpoint, colocTransport));
        var proxy = Proxy.FromConnection(connection, "/name");

        var request =  new OutgoingRequest(proxy);
        var responseTasks = new List<Task<IncomingResponse>>();

        // Act
        for (int i = 0; i < maxDispatchesPerConnection + 1; ++i)
        {
            responseTasks.Add(proxy.Invoker.InvokeAsync(request, default));
        }
        // wait for maxDispatchesPerConnection dispatches to start
        await startSemaphore.WaitAsync(maxDispatchesPerConnection);

        // Assert
        for (int i = 0; i < maxDispatchesPerConnection + 1; ++i)
        {
            Assert.That(responseTasks[i].IsCompleted, Is.False);
        }

        workSemaphore.Release(maxDispatchesPerConnection + 1);

        Assert.Multiple(async () =>
        {
            await Task.WhenAll(responseTasks);
            Assert.That(maxCount, Is.EqualTo(maxDispatchesPerConnection));
        });
    }

    // TODO: avoid duplication with InvokeAsyncTests
    private static ConnectionOptions CreateConnectionOptions(Endpoint remoteEndpoint, ColocTransport colocTransport)
    {
        var connectionOptions = new ConnectionOptions { RemoteEndpoint = remoteEndpoint };
        if (remoteEndpoint.Protocol == Protocol.Ice)
        {
            connectionOptions.SimpleClientTransport = colocTransport.ClientTransport;
        }
        else
        {
            connectionOptions.MultiplexedClientTransport = new SlicClientTransport(colocTransport.ClientTransport);
        }
        return connectionOptions;
    }

    private static ServerOptions CreateServerOptions(Endpoint endpoint, ColocTransport colocTransport)
    {
        var serverOptions = new ServerOptions { Endpoint = endpoint };
        if (endpoint.Protocol == Protocol.Ice)
        {
            serverOptions.SimpleServerTransport = colocTransport.ServerTransport;
        }
        else
        {
            serverOptions.MultiplexedServerTransport = new SlicServerTransport(colocTransport.ServerTransport);
        }
        return serverOptions;
    }
}
