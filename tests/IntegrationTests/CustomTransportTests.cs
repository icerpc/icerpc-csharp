// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using System.Net.Security;

namespace IceRpc.IntegrationTests;

public class CustomClientTransport : IMultiplexedClientTransport
{
    public string Name => "custom";

    private readonly IMultiplexedClientTransport _transport =
        new SlicClientTransport(new TcpClientTransport());

    public bool CheckParams(ServerAddress serverAddress) => true;

    public IMultiplexedConnection CreateConnection(
        ServerAddress serverAddress,
        MultiplexedConnectionOptions options,
        SslClientAuthenticationOptions? clientAuthenticationOptions)
    {
        if (serverAddress.Transport is string transport)
        {
            if (transport != "tcp" && transport != "custom")
            {
                throw new ArgumentException(
                    $"cannot use custom transport with server address '{serverAddress}'",
                    nameof(serverAddress));
            }
        }

        serverAddress = serverAddress with
        {
            Params = serverAddress.Params.Remove("custom-p"),
            Transport = "tcp"
        };

        return _transport.CreateConnection(serverAddress, options, clientAuthenticationOptions);
    }
}

public class CustomServerTransport : IMultiplexedServerTransport
{
    public string Name => "custom";

    private readonly IMultiplexedServerTransport _transport =
        new SlicServerTransport(new TcpServerTransport());

    public IListener<IMultiplexedConnection> Listen(
        ServerAddress serverAddress,
        MultiplexedConnectionOptions options,
        SslServerAuthenticationOptions? serverAuthenticationOptions,
        ILogger logger)
    {
        if (serverAddress.Transport is string transport && transport != "tcp" && transport != "custom")
        {
            throw new ArgumentException($"cannot use custom transport with server address '{serverAddress}'", nameof(serverAddress));
        }

        serverAddress = serverAddress with
        {
            Params = serverAddress.Params.Remove("custom-p"),
            Transport = "tcp"
        };

        return _transport.Listen(serverAddress, options, serverAuthenticationOptions, logger);
    }
}

public class CustomTransportTests
{
    [Test]
    public async Task CustomTransport_IcePingAsync()
    {
        await using var server = new Server(
            new ServerOptions
            {
                ServerAddress = new ServerAddress(new Uri("icerpc://127.0.0.1:0?transport=custom")),
                ConnectionOptions = new()
                {
                    Dispatcher = new MyService()
                }
            },
            multiplexedServerTransport: new CustomServerTransport());

        server.Listen();

        await using var connection = new ClientConnection(
            new ClientConnectionOptions
            {
                ServerAddress = server.ServerAddress
            },
            multiplexedClientTransport: new CustomClientTransport());

        var proxy = new ServiceProxy(connection);
        await proxy.IcePingAsync();
    }

    [Test]
    public async Task CustomTransport_UnknownServerAddressParameterAsync()
    {
        // Custom transport handles any params that start with custom-
        {
            await using var server = new Server(new ServerOptions
            {
                ServerAddress = new ServerAddress(new Uri("icerpc://127.0.0.1:0?transport=custom&custom-p=bar")),
                ConnectionOptions = new ConnectionOptions()
                {
                    Dispatcher = new MyService()
                }
            },
                multiplexedServerTransport: new CustomServerTransport());
            server.Listen();

            await using var connection1 = new ClientConnection(
                new ClientConnectionOptions
                {
                    // We add the custom server address here because listen updates the server address and the custom transport
                    // removes the parameter
                    ServerAddress = server.ServerAddress with
                    {
                        Params = server.ServerAddress.Params.Add("custom-p", "bar")
                    }
                },
                multiplexedClientTransport: new CustomClientTransport());

            var proxy = new ServiceProxy(connection1);
            await proxy.IcePingAsync();
        }
    }

    public class MyService : Service, IService
    {
    }
}
