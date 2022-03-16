// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

            this.AddScoped(serviceProvider => new TcpServerTransportOptions());

            // The default protocol is IceRpc
            this.AddScoped(_ => Protocol.IceRpc);

            // Use coloc as the default transport.
            this.UseTransport("coloc");

            // The default simple server transport is based on the configured endpoint.
            this.AddScoped(serviceProvider =>
                GetTransport(serviceProvider.GetRequiredService<Endpoint>()) switch
                {
                    "tcp" => new TcpServerTransport(serviceProvider.GetService<TcpServerTransportOptions>() ?? new()),
                    "ssl" => new TcpServerTransport(serviceProvider.GetService<TcpServerTransportOptions>() ?? new()),
                    "udp" => new UdpServerTransport(serviceProvider.GetService<UdpServerTransportOptions>() ?? new()),
                    "coloc" => serviceProvider.GetRequiredService<ColocTransport>().ServerTransport,
                    _ => ServerOptions.DefaultSimpleServerTransport
                });

            this.AddScoped(serviceProvider =>
               GetTransport(serviceProvider.GetRequiredService<Endpoint>()) switch
               {
                   "udp" => new SlicServerTransportOptions(), // i.e. invalid
                   _ => new SlicServerTransportOptions
                   {
                       SimpleServerTransport =
                          serviceProvider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>()
                   }
               });

            // The default multiplexed server transport is Slic.
            this.AddScoped<IServerTransport<IMultiplexedNetworkConnection>>(serviceProvider =>
            {
                var serverTransportOptions = serviceProvider.GetRequiredService<SlicServerTransportOptions>();
                return serverTransportOptions.SimpleServerTransport == null ?
                    new CompositeMultiplexedServerTransport() :
                    new SlicServerTransport(serverTransportOptions);
            });

            // The default simple client transport is based on the configured endpoint.
            this.AddScoped(serviceProvider =>
                GetTransport(serviceProvider.GetRequiredService<Endpoint>()) switch
                {
                    "tcp" => new TcpClientTransport(serviceProvider.GetService<TcpClientTransportOptions>() ?? new()),
                    "ssl" => new TcpClientTransport(serviceProvider.GetService<TcpClientTransportOptions>() ?? new()),
                    "udp" => new UdpClientTransport(serviceProvider.GetService<UdpClientTransportOptions>() ?? new()),
                    "coloc" => serviceProvider.GetRequiredService<ColocTransport>().ClientTransport,
                    _ => ConnectionOptions.DefaultSimpleClientTransport
                });

            this.AddScoped(serviceProvider =>
                GetTransport(serviceProvider.GetRequiredService<Endpoint>()) switch
                {
                    "udp" => new SlicClientTransportOptions(), // i.e. invalid
                    _ => new SlicClientTransportOptions
                    {
                        SimpleClientTransport =
                            serviceProvider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>()
                    }
                });

            // The default multiplexed client transport is Slic.
            this.AddScoped<IClientTransport<IMultiplexedNetworkConnection>>(serviceProvider =>
            {
                var clientTransportOptions = serviceProvider.GetRequiredService<SlicClientTransportOptions>();
                return clientTransportOptions.SimpleClientTransport == null ?
                    new CompositeMultiplexedClientTransport() :
                    new SlicClientTransport(clientTransportOptions);
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
                        certificateAuthorities: Path.Combine(Environment.CurrentDirectory, "certs", caFile))
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
