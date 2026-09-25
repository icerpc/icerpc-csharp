// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Extensions.DependencyInjection.Tests;

public sealed class ServerServiceCollectionExtensionsTests
{
    /// <summary>Verifies that two <see cref="Server" /> instances registered via DI with distinct named options each
    /// dispatch requests to their own dispatcher.</summary>
    [Test]
    public async Task Two_servers_with_distinct_named_options_dispatch_to_their_own_dispatcher()
    {
        var firstServerAddress = new ServerAddress(new Uri("icerpc://first-host"));
        var secondServerAddress = new ServerAddress(new Uri("icerpc://second-host"));

        int firstDispatchCount = 0;
        int secondDispatchCount = 0;

        var firstDispatcher = new InlineDispatcher((request, cancellationToken) =>
        {
            Interlocked.Increment(ref firstDispatchCount);
            return new(new OutgoingResponse(request));
        });

        var secondDispatcher = new InlineDispatcher((request, cancellationToken) =>
        {
            Interlocked.Increment(ref secondDispatchCount);
            return new(new OutgoingResponse(request));
        });

        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTransport()
            .AddSlicTransport()
            .AddIceRpcServer("first", firstDispatcher)
            .AddIceRpcServer("second", secondDispatcher)
            .Configure<ServerOptions>("first", options => options.ServerAddress = firstServerAddress)
            .Configure<ServerOptions>("second", options => options.ServerAddress = secondServerAddress)
            .BuildServiceProvider(validateScopes: true);

        Server[] servers = [.. provider.GetServices<Server>()];
        Assert.That(servers, Has.Length.EqualTo(2));

        foreach (Server server in servers)
        {
            _ = server.Listen();
        }

        try
        {
            await using var firstConnection = new ClientConnection(
                new ClientConnectionOptions { ServerAddress = firstServerAddress },
                multiplexedClientTransport: provider.GetRequiredService<IMultiplexedClientTransport>());

            await using var secondConnection = new ClientConnection(
                new ClientConnectionOptions { ServerAddress = secondServerAddress },
                multiplexedClientTransport: provider.GetRequiredService<IMultiplexedClientTransport>());

            using var firstRequest = new OutgoingRequest(new ServiceAddress(firstServerAddress.Protocol));
            IncomingResponse firstResponse = await firstConnection.InvokeAsync(firstRequest);

            using var secondRequest = new OutgoingRequest(new ServiceAddress(secondServerAddress.Protocol));
            IncomingResponse secondResponse = await secondConnection.InvokeAsync(secondRequest);

            Assert.That(firstResponse.StatusCode, Is.EqualTo(StatusCode.Ok));
            Assert.That(secondResponse.StatusCode, Is.EqualTo(StatusCode.Ok));
            Assert.That(firstDispatchCount, Is.EqualTo(1));
            Assert.That(secondDispatchCount, Is.EqualTo(1));
        }
        finally
        {
            foreach (Server server in servers)
            {
                await server.ShutdownAsync();
            }
        }
    }
}
