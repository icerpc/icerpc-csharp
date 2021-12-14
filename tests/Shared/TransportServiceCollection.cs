// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Tests
{
    public class TransportServiceCollection : ServiceCollection
    {
        public TransportServiceCollection()
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

            // The default protocol is Ice2
            this.AddScoped(_ => Protocol.Ice2);

            // Use coloc as the default transport.
            this.UseTransport("coloc");

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

            // The simple server transport listener
            this.AddScoped(serviceProvider =>
                serviceProvider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>().Listen(
                    serviceProvider.GetRequiredService<Endpoint>(),
                    serviceProvider.GetRequiredService<ILogger>()));

            // The multiplexed server transport listener
            this.AddScoped(serviceProvider =>
                serviceProvider.GetRequiredService<IServerTransport<IMultiplexedNetworkConnection>>().Listen(
                    serviceProvider.GetRequiredService<Endpoint>(),
                    serviceProvider.GetRequiredService<ILogger>()));
        }
    }

    public static class TransportServiceCollectionExtensions
    {
        public static IServiceCollection UseColoc(
            this IServiceCollection collection,
            ColocTransport transport,
            int port) =>
            collection.AddScoped(_ => transport).UseTransport("coloc", port);

        public static IServiceCollection UseTls(
            this IServiceCollection collection,
            string caFile = "cacert.der",
            string certificateFile = "server.p12")
        {
            // The certificate target host name is 127.0.0.1
            collection.AddTransient<Endpoint>(_ => $"ice+tcp://127.0.0.1:0");

            collection.AddScoped(_ => new SslClientAuthenticationOptions()
            {
                RemoteCertificateValidationCallback =
                    CertificateValidaton.GetServerCertificateValidationCallback(
                        certificateAuthorities: new X509Certificate2Collection
                        {
                            new X509Certificate2(Path.Combine(Environment.CurrentDirectory, "certs", caFile))
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

        public static IServiceCollection UseTransport(
            this IServiceCollection collection,
            string transport,
            int port = 0) =>
            collection.AddScoped(serviceProvider =>
            {
                // Get the default endpoint for the given transport.
                Endpoint endpoint = transport switch
                {
                    "tcp" => $"ice+tcp://[::1]:{port}",
                    "ssl" => $"ice+tcp://[::1]:{port}",
                    "udp" => $"ice+udp://[::1]:{port}",
                    "coloc" => $"ice+coloc://coloctest:{port}",
                    _ => Server.DefaultSimpleServerTransport.DefaultEndpoint with { Port = (ushort)port }
                };

                // Set the endpoint protocol to the configured protocol.
                if (endpoint.Transport == "udp")
                {
                    // UDP is only supported with Ice1
                    endpoint = endpoint with { Protocol = Protocol.Ice1 };
                }
                else
                {
                    endpoint = endpoint with { Protocol = serviceProvider.GetRequiredService<Protocol>() };
                }

                // For tcp or ssl, set the "tls" parameter to true if authentication options are configured, to false
                // otherwise.
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
                }

                return endpoint;
            });
    }
}
