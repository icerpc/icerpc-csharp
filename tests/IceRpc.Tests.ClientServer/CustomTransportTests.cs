// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace IceRpc.Tests.ClientServer
{
    public class CustomClientTransport : IClientTransport
    {
        private readonly IClientTransport _transport = new TcpClientTransport();

        public MultiStreamConnection CreateConnection(
            Endpoint remoteEndpoint,
            ClientConnectionOptions connectionOptions,
            ILoggerFactory loggerFactory)
        {
            if (remoteEndpoint.Transport == "custom")
            {
                Endpoint newEndpoint = remoteEndpoint with
                {
                    Params = remoteEndpoint.Params.RemoveAll(
                        p => p.Name.StartsWith("custom-", StringComparison.Ordinal))
                };
                return _transport.CreateConnection(newEndpoint, connectionOptions, loggerFactory);
            }
            else
            {
                throw new UnknownTransportException(remoteEndpoint.Transport, remoteEndpoint.Protocol);
            }
        }
    }

    public class CustomServerTransport : IServerTransport
    {
        private readonly IServerTransport _transport = new TcpServerTransport();

        public (IListener? Listener, MultiStreamConnection? Connection) Listen(
            Endpoint endpoint,
            ServerConnectionOptions connectionOptions,
            ILoggerFactory loggerFactory)
        {
            if (endpoint.Transport == "custom")
            {
                Endpoint newEndpoint = endpoint with
                {
                    Params = endpoint.Params.RemoveAll(p => p.Name.StartsWith("custom-", StringComparison.Ordinal))
                };
                return _transport.Listen(newEndpoint, connectionOptions, loggerFactory);
            }
            else
            {
                throw new UnknownTransportException(endpoint.Transport, endpoint.Protocol);
            }
        }
    }

    public class CustomTransportTests
    {
        [Test]
        public async Task CustomTransport_IcePingAsync()
        {
            await using var server = new Server
            {
                ServerTransport = new CustomServerTransport(),
                Endpoint = "ice+custom://127.0.0.1:0?tls=false",
                Dispatcher = new MyService()
            };

            server.Listen();

            await using var connection = new Connection
            {
                ClientTransport = new CustomClientTransport(),
                RemoteEndpoint = server.Endpoint
            };

            var prx = ServicePrx.FromConnection(connection);
            await prx.IcePingAsync();
        }

        [Test]
        public async Task CustomTransport_UnknownEndpointParameterAsync()
        {
            // Using an unknown parameter with tcp transport results in FormatException
            {
                await using var server = new Server
                {
                    ServerTransport = new TcpServerTransport(),
                    Endpoint = "ice+tcp://127.0.0.1:0?tls=false&custom-p=bar",
                    Dispatcher = new MyService()
                };
                Assert.Throws<FormatException>(() => server.Listen());
            }

            // Custom transport handles any params that start with custom-
            {
                await using var server = new Server
                {
                    ServerTransport = new CustomServerTransport(),
                    Endpoint = "ice+custom://127.0.0.1:0?tls=false&custom-p=bar",
                    Dispatcher = new MyService()
                };
                server.Listen();

                await using var connection1 = new Connection
                {
                    ClientTransport = new CustomClientTransport(),
                    // We add the custom endpoint here because listen updates the endpoint and the custom transport
                    // removes the parameter
                    RemoteEndpoint = server.Endpoint with
                    {
                        Params = server.Endpoint.Params.Add(new EndpointParam("custom-p", "bar"))
                    }
                };

                var prx = ServicePrx.FromConnection(connection1);
                await prx.IcePingAsync();

                await using var connection2 = new Connection
                {
                    ClientTransport = new TcpClientTransport(),
                    // We add the custom endpoint here because listen updates the endpoint and the custom transport
                    // removes the parameter
                    RemoteEndpoint = server.Endpoint with
                    {
                        Params = server.Endpoint.Params.Add(new EndpointParam("custom-p", "bar"))
                    }
                };

                prx = ServicePrx.FromConnection(connection2);
                Assert.ThrowsAsync<FormatException>(async () => await prx.IcePingAsync());
            }
        }

        public class MyService : Service, IService
        {
        }
    }
}
