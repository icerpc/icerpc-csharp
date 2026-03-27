// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Transports;
using IceRpc.Transports.Slic;
using IceRpc.Transports.Tcp;
using NUnit.Framework;
using System.Net.Security;

namespace IceRpc.IntegrationTests;

public class CustomClientTransport : IMultiplexedClientTransport
{
    public string DefaultName => "custom";

    public bool IsSslRequired(string? transportName) => false;

    private readonly IMultiplexedClientTransport _transport =
        new SlicClientTransport(new TcpClientTransport());

    public IMultiplexedConnection CreateConnection(
        TransportAddress transportAddress,
        MultiplexedConnectionOptions options,
        SslClientAuthenticationOptions? clientAuthenticationOptions)
    {
        if (transportAddress.TransportName is string name && name is not "custom" and not "tcp")
        {
            throw new NotSupportedException(
                $"The custom client transport does not support transport '{name}'.");
        }

        // Remap custom transport name to tcp and strip custom params before delegating.
        transportAddress = transportAddress with
        {
            TransportName = "tcp",
            Params = transportAddress.Params.Remove("custom-p")
        };

        return _transport.CreateConnection(transportAddress, options, clientAuthenticationOptions);
    }
}

public class CustomServerTransport : IMultiplexedServerTransport
{
    public string DefaultName => "custom";

    private readonly IMultiplexedServerTransport _transport =
        new SlicServerTransport(new TcpServerTransport());

    public IListener<IMultiplexedConnection> Listen(
        TransportAddress transportAddress,
        MultiplexedConnectionOptions options,
        SslServerAuthenticationOptions? serverAuthenticationOptions)
    {
        if (transportAddress.TransportName is string name && name is not "custom" and not "tcp")
        {
            throw new NotSupportedException(
                $"The custom server transport does not support transport '{name}'.");
        }

        // Remap custom transport name to tcp and strip custom params before delegating.
        transportAddress = transportAddress with
        {
            TransportName = "tcp",
            Params = transportAddress.Params.Remove("custom-p")
        };

        return _transport.Listen(transportAddress, options, serverAuthenticationOptions);
    }
}

public partial class CustomTransportTests
{
    [Test]
    public async Task CustomTransport_PingAsync()
    {
        await using var server = new Server(
            new ServerOptions
            {
                ServerAddress = new ServerAddress(new Uri("icerpc://127.0.0.1:0?transport=custom")),
                ConnectionOptions = new()
                {
                    Dispatcher = new PingableService()
                }
            },
            multiplexedServerTransport: new CustomServerTransport());

        ServerAddress serverAddress = server.Listen();

        await using var connection = new ClientConnection(
            new ClientConnectionOptions
            {
                ServerAddress = serverAddress
            },
            multiplexedClientTransport: new CustomClientTransport());

        var proxy = new PingableProxy(connection, new Uri($"icerpc:{PingableProxy.DefaultServicePath}"));
        await proxy.PingAsync();
    }

    [Test]
    public async Task CustomTransport_UnknownServerAddressParameterAsync()
    {
        // Custom transport handles any params that start with custom-
        {
            await using var server = new Server(
                new ServerOptions
                {
                    ServerAddress = new ServerAddress(new Uri("icerpc://127.0.0.1:0?transport=custom&custom-p=bar")),
                    ConnectionOptions = new ConnectionOptions()
                    {
                        Dispatcher = new PingableService()
                    }
                },
                multiplexedServerTransport: new CustomServerTransport());

            ServerAddress serverAddress = server.Listen();

            await using var connection1 = new ClientConnection(
                new ClientConnectionOptions
                {
                    // We add the custom server address here because listen updates the server address and the custom transport
                    // removes the parameter
                    ServerAddress = serverAddress with
                    {
                        Params = serverAddress.Params.Add("custom-p", "bar")
                    }
                },
                multiplexedClientTransport: new CustomClientTransport());

            var proxy = new PingableProxy(connection1, new Uri($"icerpc:{PingableProxy.DefaultServicePath}"));
            await proxy.PingAsync();
        }
    }

    [Service]
    public partial class PingableService : IPingableService
    {
        public ValueTask PingAsync(IFeatureCollection features, CancellationToken cancellationToken) => default;
    }
}
