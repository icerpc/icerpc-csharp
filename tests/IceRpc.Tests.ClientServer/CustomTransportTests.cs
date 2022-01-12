// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace IceRpc.Tests.ClientServer
{
    public class CustomClientTransport : IClientTransport<IMultiplexedNetworkConnection>
    {
        private readonly IClientTransport<IMultiplexedNetworkConnection> _transport =
            new SlicClientTransport(new TcpClientTransport());

        public IMultiplexedNetworkConnection CreateConnection(Endpoint remoteEndpoint, ILogger logger)
        {
            if (remoteEndpoint.Transport == "custom")
            {
                Endpoint newEndpoint = remoteEndpoint with
                {
                    Params = remoteEndpoint.Params.Remove("custom-p")
                };
                return _transport.CreateConnection(newEndpoint, logger);
            }
            else
            {
                throw new UnknownTransportException(remoteEndpoint.Transport);
            }
        }
    }

    public class CustomServerTransport : IServerTransport<IMultiplexedNetworkConnection>
    {
        public Endpoint DefaultEndpoint => "icerpc://[::0]?transport=custom";

        private readonly IServerTransport<IMultiplexedNetworkConnection> _transport =
            new SlicServerTransport(new TcpServerTransport());

        public IListener<IMultiplexedNetworkConnection> Listen(Endpoint endpoint, ILogger logger)
        {
            if (endpoint.Transport == "custom")
            {
                Endpoint newEndpoint = endpoint with
                {
                    Params = endpoint.Params.Remove("custom-p")
                };
                return _transport.Listen(newEndpoint, logger);
            }
            else
            {
                throw new UnknownTransportException(endpoint.Transport);
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
                MultiplexedServerTransport = new CustomServerTransport(),
                Endpoint = "icerpc://127.0.0.1:0?tls=false&transport=custom",
                Dispatcher = new MyService()
            };

            server.Listen();

            await using var connection = new Connection
            {
                MultiplexedClientTransport = new CustomClientTransport(),
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
                    MultiplexedServerTransport = new SlicServerTransport(new TcpServerTransport()),
                    Endpoint = "icerpc://127.0.0.1:0?tls=false&custom-p=bar",
                    Dispatcher = new MyService()
                };
                Assert.Throws<FormatException>(() => server.Listen());
            }

            // Custom transport handles any params that start with custom-
            {
                await using var server = new Server
                {
                    MultiplexedServerTransport = new CustomServerTransport(),
                    Endpoint = "icerpc://127.0.0.1:0?tls=false&transport=custom&custom-p=bar",
                    Dispatcher = new MyService()
                };
                server.Listen();

                await using var connection1 = new Connection
                {
                    MultiplexedClientTransport = new CustomClientTransport(),
                    // We add the custom endpoint here because listen updates the endpoint and the custom transport
                    // removes the parameter
                    RemoteEndpoint = server.Endpoint with
                    {
                        Params = server.Endpoint.Params.Add("custom-p", "bar")
                    }
                };

                var prx = ServicePrx.FromConnection(connection1);
                await prx.IcePingAsync();

                await using var connection2 = new Connection
                {
                    MultiplexedClientTransport = new SlicClientTransport(new TcpClientTransport()),
                    // We add the custom endpoint here because listen updates the endpoint and the custom transport
                    // removes the parameter
                    RemoteEndpoint = server.Endpoint with
                    {
                        Params = server.Endpoint.Params.Add("custom-p", "bar")
                    }
                };

                prx = ServicePrx.FromConnection(connection2);
                Assert.ThrowsAsync<FormatException>(async () => await prx.IcePingAsync());
            }
        }

        [Test]
        public async Task CustomTransport_DefaultEndpointAsync()
        {
            await using var server = new Server
            {
                MultiplexedServerTransport = new CustomServerTransport(),
                Dispatcher = new MyService()
            };
            Endpoint defaultEndpoint = server.MultiplexedServerTransport.DefaultEndpoint;
            Assert.That(server.Endpoint, Is.EqualTo(defaultEndpoint));
            server.Listen();
            Assert.That(server.Endpoint, Is.EqualTo(defaultEndpoint));
        }

        public class MyService : Service, IService
        {
        }
    }
}
