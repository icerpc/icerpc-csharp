// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using System.Net.Security;

namespace IceRpc.IntegrationTests;

public class CustomClientTransport : IClientTransport<IMultiplexedConnection>
{
    public string Name => "custom";

    private readonly IClientTransport<IMultiplexedConnection> _transport =
        new SlicClientTransport(new TcpClientTransport());

    public bool CheckParams(Endpoint endpoint) => true;

    public IMultiplexedConnection CreateConnection(
        Endpoint endpoint,
        SslClientAuthenticationOptions? authenticationOptions,
        ILogger logger)
    {
        if (endpoint.Params.TryGetValue("transport", out string? endpointTransport))
        {
            if (endpointTransport != "tcp" && endpointTransport != "custom")
            {
                throw new ArgumentException(
                    $"cannot use custom transport with endpoint '{endpoint}'",
                    nameof(endpoint));
            }
        }

        endpoint = endpoint with
        {
            Params = endpoint.Params.Remove("custom-p").SetItem("transport", "tcp")
        };

        return _transport.CreateConnection(endpoint, authenticationOptions, logger);
    }
}

public class CustomServerTransport : IServerTransport<IMultiplexedConnection>
{
    public string Name => "custom";

    private readonly IServerTransport<IMultiplexedConnection> _transport =
        new SlicServerTransport(new TcpServerTransport());

    public IListener<IMultiplexedConnection> Listen(
        Endpoint endpoint,
        SslServerAuthenticationOptions? authenticationOptions,
        ILogger logger)
    {
        if (endpoint.Params.TryGetValue("transport", out string? endpointTransport))
        {
            if (endpointTransport != "tcp" && endpointTransport != "custom")
            {
                throw new ArgumentException(
                    $"cannot use custom transport with endpoint '{endpoint}'",
                    nameof(endpoint));
            }
        }

        endpoint = endpoint with
        {
            Params = endpoint.Params.Remove("custom-p").SetItem("transport", "tcp")
        };
        return _transport.Listen(endpoint, authenticationOptions, logger);
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
                Endpoint = new Endpoint(new Uri("icerpc://127.0.0.1:0?transport=custom")),
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
                Endpoint = server.Endpoint
            },
            multiplexedClientTransport: new CustomClientTransport());

        var proxy = new ServiceProxy(connection);
        await proxy.IcePingAsync();
    }

    [Test]
    public async Task CustomTransport_UnknownEndpointParameterAsync()
    {
        // Custom transport handles any params that start with custom-
        {
            await using var server = new Server(new ServerOptions
            {
                Endpoint = new Endpoint(new Uri("icerpc://127.0.0.1:0?transport=custom&custom-p=bar")),
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
                    // We add the custom endpoint here because listen updates the endpoint and the custom transport
                    // removes the parameter
                    Endpoint = server.Endpoint with
                    {
                        Params = server.Endpoint.Params.Add("custom-p", "bar")
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
