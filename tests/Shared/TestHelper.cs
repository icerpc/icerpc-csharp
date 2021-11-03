// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Tests
{
    public static class TestHelper
    {
        private static ulong _counter;

        public static string EscapeIPv6Address(string address, Protocol? protocol) =>
            (protocol?.Code ?? ProtocolCode.Ice2) switch
            {
                ProtocolCode.Ice1 =>
                    address.Contains(':', StringComparison.InvariantCulture) ? $"\"{address}\"" : address,
                _ => address.Contains(':', StringComparison.InvariantCulture) ? $"[{address}]" : address
            };

        public static IClientTransport<IMultiplexedNetworkConnection> GetSecureMultiplexedClientTransport(
            string caFile = "cacert.der") =>
            new SlicClientTransport(
                    new TcpClientTransport(authenticationOptions:
                    new()
                    {
                        RemoteCertificateValidationCallback =
                            CertificateValidaton.GetServerCertificateValidationCallback(
                                certificateAuthorities: new X509Certificate2Collection
                                {
                                    new X509Certificate2(Path.Combine(Environment.CurrentDirectory, "certs", caFile))
                                })
                    }));

        public static IServerTransport<IMultiplexedNetworkConnection> GetSecureMultiplexedServerTransport(
            string certificateFile = "server.p12") =>
             new SlicServerTransport(
                new TcpServerTransport(authenticationOptions:
                    new()
                    {
                        ServerCertificate = new X509Certificate2(
                            Path.Combine(Environment.CurrentDirectory, "certs", certificateFile),
                            "password")
                    }));

        public static Endpoint GetTestEndpoint(
             string host = "127.0.0.1",
             int port = 0,
             string transport = "tcp",
             bool tls = false,
             Protocol? protocol = null)
        {
            if (transport == "coloc" && host == "127.0.0.1" && port == 0)
            {
                return GetUniqueColocEndpoint(protocol);
            }

            if (protocol == Protocol.Ice1)
            {
                return $"{transport} -h {EscapeIPv6Address(host, protocol)} -p {port}";
            }
            else
            {
                string endpoint = $"ice+{transport}://{EscapeIPv6Address(host, protocol)}:{port}";
                if (transport == "tcp" && !tls)
                {
                    endpoint = $"{endpoint}?tls=false";
                }
                return endpoint;
            }
        }

        public static string GetTestProxy(
            string path,
            string host = "127.0.0.1",
            int port = 0,
            string transport = "tcp",
            bool tls = false,
            Protocol? protocol = null)
        {
            if (protocol == Protocol.Ice1)
            {
                return $"{path}:{transport} -h {EscapeIPv6Address(host, protocol)} -p {port}";
            }
            else
            {
                string proxy = $"ice+{transport}://{EscapeIPv6Address(host, protocol)}:{port}{path}";
                if (transport == "tcp" && !tls)
                {
                    proxy = $"{proxy}?tls=false";
                }
                return proxy;
            }
        }

        public static Endpoint GetUniqueColocEndpoint(Protocol? protocol = null) =>
            protocol == Protocol.Ice1 ? $"coloc -h test.{Interlocked.Increment(ref _counter)}" :
            $"ice+coloc://test.{Interlocked.Increment(ref _counter)}";

        public static IClientTransport<IMultiplexedNetworkConnection> CreateMultiplexedClientTransport(
            string transport = "tcp",
            TcpOptions? options = null,
            SslClientAuthenticationOptions? authenticationOptions = null,
            SlicOptions? slicOptions = null,
            ILoggerFactory? _ = null)
        {
            // TODO: give loggerFactory to SlicClientTransport
            return transport switch
                {
                    "tcp" => new SlicClientTransport(new TcpClientTransport(options ?? new(), authenticationOptions),
                                                     slicOptions ?? new SlicOptions()),
                    "coloc" => new SlicClientTransport(new ColocClientTransport(), slicOptions ?? new SlicOptions()),
                    _ => throw new UnknownTransportException(transport)
                };
        }

        public static IServerTransport<IMultiplexedNetworkConnection> CreateMultiplexedServerTransport(
            string transport = "tcp",
            TcpOptions? options = null,
            SslServerAuthenticationOptions? authenticationOptions = null,
            SlicOptions? slicOptions = null,
            ILoggerFactory? _ = null)
        {
            // TODO: give loggerFactory to SlicServerTransport
            return transport switch
                {
                    "tcp" => new SlicServerTransport(new TcpServerTransport(options ?? new(), authenticationOptions),
                                                     slicOptions ?? new SlicOptions()),
                    "coloc" => new SlicServerTransport(new ColocServerTransport(), slicOptions ?? new SlicOptions()),
                    _ => throw new UnknownTransportException(transport)
                };
        }

        public static IClientTransport<ISimpleNetworkConnection> CreateSimpleClientTransport(
            string transport = "tcp",
            object? options = null,
            SslClientAuthenticationOptions? authenticationOptions = null)
        {
            return transport switch
                {
                    "tcp" => new TcpClientTransport((TcpOptions?)options ?? new(), authenticationOptions),
                    "ssl" => new TcpClientTransport((TcpOptions?)options ?? new(), authenticationOptions),
                    "udp" => new UdpClientTransport((UdpClientOptions?)options ?? new()),
                    "coloc" => new ColocClientTransport(),
                    _ => throw new UnknownTransportException(transport)
                };
        }

        public static IServerTransport<ISimpleNetworkConnection> CreateSimpleServerTransport(
            string transport = "tcp",
            object? options = null,
            SslServerAuthenticationOptions? authenticationOptions = null)
        {
            return transport switch
                {
                    "tcp" => new TcpServerTransport((TcpOptions?)options ?? new(), authenticationOptions),
                    "ssl" => new TcpServerTransport((TcpOptions?)options ?? new(), authenticationOptions),
                    "udp" => new UdpServerTransport((UdpServerOptions?)options ?? new()),
                    "coloc" => new ColocServerTransport(),
                    _ => throw new UnknownTransportException(transport)
                };
        }
    }
}
