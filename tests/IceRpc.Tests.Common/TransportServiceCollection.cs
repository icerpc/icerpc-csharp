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

            this.AddScoped(serviceProvider => new TcpServerOptions());

            // The default protocol is IceRpc
            this.AddScoped(_ => Protocol.IceRpc);

            // Use coloc as the default transport.
            this.UseTransport("coloc");

            // The default simple server transport is based on the configured endpoint.
            this.AddScoped(serviceProvider =>
                GetTransport(serviceProvider.GetRequiredService<Endpoint>()) switch
                {
                    "tcp" => new TcpServerTransport(serviceProvider.GetService<TcpServerOptions>() ?? new()),
                    "ssl" => new TcpServerTransport(serviceProvider.GetService<TcpServerOptions>() ?? new()),
                    "udp" => new UdpServerTransport(serviceProvider.GetService<UdpServerOptions>() ?? new()),
                    "coloc" => serviceProvider.GetRequiredService<ColocTransport>().ServerTransport,
                    _ => ServerOptions.DefaultSimpleServerTransport
                });

            // The default multiplexed server transport is Slic.
            this.AddScoped<IServerTransport<IMultiplexedNetworkConnection>>(serviceProvider =>
                GetTransport(serviceProvider.GetRequiredService<Endpoint>()) switch
                {
                    "udp" => new CompositeMultiplexedServerTransport(),
                    _ => new SlicServerTransport(
                        serviceProvider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>(),
                        serviceProvider.GetService<SlicOptions>() ?? new())
                });

            // The default simple client transport is based on the configured endpoint.
            this.AddScoped(serviceProvider =>
                GetTransport(serviceProvider.GetRequiredService<Endpoint>()) switch
                {
                    "tcp" => new TcpClientTransport(serviceProvider.GetService<TcpClientOptions>() ?? new()),
                    "ssl" => new TcpClientTransport(serviceProvider.GetService<TcpClientOptions>() ?? new()),
                    "udp" => new UdpClientTransport(serviceProvider.GetService<UdpClientOptions>() ?? new()),
                    "coloc" => serviceProvider.GetRequiredService<ColocTransport>().ClientTransport,
                    _ => ConnectionOptions.DefaultSimpleClientTransport
                });

            // The default multiplexed client transport is Slic.
            this.AddScoped<IClientTransport<IMultiplexedNetworkConnection>>(serviceProvider =>
                GetTransport(serviceProvider.GetRequiredService<Endpoint>()) switch
                {
                    "udp" => new CompositeMultiplexedClientTransport(),
                    _ => new SlicClientTransport(
                        serviceProvider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>(),
                        serviceProvider.GetService<SlicOptions>() ?? new())
                });

            // TODO: would be nicer to have a null default
            static string? GetTransport(Endpoint endpoint) =>
                endpoint.Params.TryGetValue("transport", out string? value) ? value : "tcp";
        }
    }

    public static class TransportServiceCollectionExtensions
    {
        public static IServiceCollection UseColoc(
            this IServiceCollection collection,
            ColocTransport transport,
            int port) =>
            collection.AddScoped(_ => transport).UseEndpoint("coloc", host: "coloctest", port);

        public static IServiceCollection UseTls(
            this IServiceCollection collection,
            string caFile = "cacert.der",
            string certificateFile = "server.p12")
        {
            collection.AddScoped(serviceProvider => new SslClientAuthenticationOptions()
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

        public static IServiceCollection UseTransport(this IServiceCollection collection, string transport) =>
            collection.UseEndpoint(transport, "[::1]", 0);

        public static IServiceCollection UseEndpoint(
            this IServiceCollection collection,
            string transport,
            string host,
            int port)
        {
            collection.AddScoped(
                typeof(Endpoint),
                serviceProvider =>
                {
                    Protocol protocol = serviceProvider.GetRequiredService<Protocol>();
                    return Endpoint.FromString($"{protocol}://{host}:{port}?transport={transport}");
                });

            return collection;
        }
    }
}
