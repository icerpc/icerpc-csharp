// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Immutable;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Tests
{
    public class IntegrationTestServiceCollection : ServiceCollection
    {
        public IntegrationTestServiceCollection()
        {
            this.AddScoped<ColocTransport>();

            this.AddScoped<ILoggerFactory>(_ => LogAttributeLoggerFactory.Instance);

            this.AddScoped(serviceProvider =>
                serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Test"));

            this.AddScoped(serviceProvider =>
                new TcpServerOptions
                {
                    AuthenticationOptions = serviceProvider.GetService<SslServerAuthenticationOptions>()
                });

            this.AddScoped(serviceProvider =>
                new TcpClientOptions
                {
                    AuthenticationOptions = serviceProvider.GetService<SslClientAuthenticationOptions>()
                });

            // Default protocol is Ice2
            this.AddScoped(_ => Protocol.Ice2);

            // The default endpoint is the coloc transport default endpoint.
            this.AddScoped(serviceProvider =>
                serviceProvider.GetRequiredService<ColocTransport>().ServerTransport.DefaultEndpoint);

            // The default simple server transport is based on the transport of the service collection endpoint.
            this.AddScoped(serviceProvider =>
                serviceProvider.GetRequiredService<Endpoint>().Transport switch
                {
                    "tcp" => new TcpServerTransport(serviceProvider.GetService<TcpServerOptions>() ?? new()),
                    "ssl" => new TcpServerTransport(serviceProvider.GetService<TcpServerOptions>() ?? new()),
                    "udp" => new UdpServerTransport(serviceProvider.GetService<UdpServerOptions>() ?? new()),
                    "coloc" => serviceProvider.GetRequiredService<ColocTransport>().ServerTransport,
                    _ => Server.DefaultSimpleServerTransport
                });

            // The default multiplexed server transport is Slic.
            this.AddScoped<IServerTransport<IMultiplexedNetworkConnection>>(serviceProvider =>
                serviceProvider.GetRequiredService<Endpoint>().Transport switch
                {
                    "udp" => new CompositeMultiplexedServerTransport(),
                    _ => new SlicServerTransport(
                        serviceProvider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>(),
                        serviceProvider.GetService<SlicOptions>() ?? new())
                });

            // The default Server instance.
            this.AddScoped(serviceProvider =>
            {
                IServerTransport<ISimpleNetworkConnection> simpleServerTransport =
                    serviceProvider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>();
                IServerTransport<IMultiplexedNetworkConnection> multiplexedServerTransport =
                    serviceProvider.GetRequiredService<IServerTransport<IMultiplexedNetworkConnection>>();

                Endpoint endpoint = serviceProvider.GetRequiredService<Endpoint>();
                if (endpoint.Transport == "udp")
                {
                    // UDP is only supported with Ice1
                    endpoint = endpoint with { Protocol = Protocol.Ice1 };
                }
                else
                {
                    endpoint = endpoint with { Protocol = serviceProvider.GetRequiredService<Protocol>() };
                }

                if (endpoint.Transport == "tcp" || endpoint.Transport == "ssl")
                {
                    // If server authentication options are set, use TLS
                    bool tls = serviceProvider.GetService<SslServerAuthenticationOptions>() != null;
                    if (endpoint.Protocol != Protocol.Ice1)
                    {
                        endpoint = endpoint with
                        {
                            Params = ImmutableList.Create(new EndpointParam("tls", tls.ToString().ToLowerInvariant()))
                        };
                    }

                    // Always use system assigned port when using TCP if default port is set.
                    if (endpoint.Port == 4062)
                    {
                        endpoint = endpoint with { Port = 0 };
                    }
                }

                var server = new Server
                {
                    Dispatcher = serviceProvider.GetService<IDispatcher>(),
                    Endpoint = endpoint,
                    SimpleServerTransport = simpleServerTransport,
                    MultiplexedServerTransport = multiplexedServerTransport,
                    ConnectionOptions = serviceProvider.GetService<ConnectionOptions>() ?? new(),
                    LoggerFactory = serviceProvider.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance
                };
                server.Listen();
                return server;
            });

            // The default simple client transport is based on the transport of the service collection endpoint.
            this.AddScoped(serviceProvider =>
                serviceProvider.GetRequiredService<Endpoint>().Transport switch
                {
                    "tcp" => new TcpClientTransport(serviceProvider.GetService<TcpClientOptions>() ?? new()),
                    "ssl" => new TcpClientTransport(serviceProvider.GetService<TcpClientOptions>() ?? new()),
                    "udp" => new UdpClientTransport(serviceProvider.GetService<UdpClientOptions>() ?? new()),
                    "coloc" => serviceProvider.GetRequiredService<ColocTransport>().ClientTransport,
                    _ => Connection.DefaultSimpleClientTransport
                });

            // The default multiplexed client transport is Slic.
            this.AddScoped<IClientTransport<IMultiplexedNetworkConnection>>(serviceProvider =>
                serviceProvider.GetRequiredService<Endpoint>().Transport switch
                {
                    "udp" => new CompositeMultiplexedClientTransport(),
                    _ => new SlicClientTransport(
                        serviceProvider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>(),
                        serviceProvider.GetService<SlicOptions>() ?? new())
                });

            // The default Connection is configured to connection to the Server configured on the service collection.
            this.AddScoped(serviceProvider =>
            {
                IClientTransport<ISimpleNetworkConnection> simpleClientTransport =
                    serviceProvider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>();

                IClientTransport<IMultiplexedNetworkConnection> multiplexedClientTransport =
                    serviceProvider.GetRequiredService<IClientTransport<IMultiplexedNetworkConnection>>();

                return new Connection
                {
                    RemoteEndpoint = serviceProvider.GetRequiredService<Server>().Endpoint,
                    SimpleClientTransport = simpleClientTransport,
                    MultiplexedClientTransport = multiplexedClientTransport,
                    Options = serviceProvider.GetService<ConnectionOptions>() ?? new(),
                    LoggerFactory = serviceProvider.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance
                };
            });

            // The default ConnectionPool is configured with the client transport from the service collection.
            this.AddScoped(serviceProvider =>
            {
                IClientTransport<ISimpleNetworkConnection> simpleClientTransport =
                    serviceProvider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>();

                IClientTransport<IMultiplexedNetworkConnection> multiplexedClientTransport =
                    serviceProvider.GetRequiredService<IClientTransport<IMultiplexedNetworkConnection>>();

                return new ConnectionPool
                {
                    SimpleClientTransport = simpleClientTransport,
                    MultiplexedClientTransport = multiplexedClientTransport,
                    ConnectionOptions = serviceProvider.GetService<ConnectionOptions>() ?? new(),
                    LoggerFactory = serviceProvider.GetService<ILoggerFactory>() ?? LogAttributeLoggerFactory.Instance
                };
            });

            // The default proxy is created from the Connection configured on the service collection.
            this.AddScoped(serviceProvider => Proxy.FromConnection(
                serviceProvider.GetRequiredService<Connection>(),
                path: "/",
                invoker: serviceProvider.GetService<IInvoker>()));
        }
    }

    static class IntegrationTestServiceCollectionExtentions
    {
        public static IServiceCollection UseProtocol(this IServiceCollection collection, ProtocolCode protocolCode) =>
            collection.AddScoped(_ => Protocol.FromProtocolCode(protocolCode));

        public static IServiceCollection UseTcp(this IServiceCollection collection) =>
            collection.AddScoped(serviceProvider => new TcpServerTransport().DefaultEndpoint);

        public static IServiceCollection UseColoc(
            this IServiceCollection collection,
            ColocTransport transport,
            int port) =>
            collection.AddScoped(_ => transport).AddScoped<Endpoint>(_ => $"ice+coloc://colochost:{port}");

        public static IServiceCollection UseTls(
            this IServiceCollection collection,
            string caFile = "cacert.der",
            string certificateFile = "server.p12")
        {
            collection.AddScoped(_ => new SslClientAuthenticationOptions()
                {
                    RemoteCertificateValidationCallback =
                    CertificateValidaton.GetServerCertificateValidationCallback(
                        certificateAuthorities: new X509Certificate2Collection
                        {
                            new X509Certificate2(Path.Combine(
                                Environment.CurrentDirectory,
                                "certs",
                                caFile))
                        })
                });

            collection.AddScoped(_ => new SslServerAuthenticationOptions()
            {
                ServerCertificate = new X509Certificate2(
                    Path.Combine(Environment.CurrentDirectory, "certs", certificateFile),
                    "password")
            });

            return collection;
        }

        public static IServiceCollection UseVoidDispatcher(this IServiceCollection collection) =>
            collection.AddTransient<IDispatcher>(_ => new InlineDispatcher((request, cancel) =>
                new(OutgoingResponse.ForPayload(request, ReadOnlyMemory<ReadOnlyMemory<byte>>.Empty))));
    }

    public static class IntegrationTestServiceProviderExtensions
    {
        public static T GetProxy<T>(this IServiceProvider serviceProvider, string? path = null) where T : IPrx, new()
        {
            Proxy proxy = serviceProvider.GetRequiredService<Proxy>().Clone();
            return new T { Proxy = proxy.WithPath(path ?? typeof(T).GetDefaultPath()) };
        }
    }
}
