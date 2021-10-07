// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Tests
{
    public static class TestHelper
    {
        private static ulong _counter;

        public static string EscapeIPv6Address(string address, ProtocolCode protocol) =>
            protocol switch
            {
                ProtocolCode.Ice1 => address.Contains(':', StringComparison.InvariantCulture) ? $"\"{address}\"" : address,
                _ => address.Contains(':', StringComparison.InvariantCulture) ? $"[{address}]" : address
            };

        public static IClientTransport GetSecureClientTransport(string caFile = "cacert.der") =>
            new TcpClientTransport(authenticationOptions:
                new()
                {
                    RemoteCertificateValidationCallback =
                        CertificateValidaton.GetServerCertificateValidationCallback(
                            certificateAuthorities: new X509Certificate2Collection
                            {
                                new X509Certificate2(Path.Combine(Environment.CurrentDirectory, "certs", caFile))
                            })
                });

        public static IServerTransport GetSecureServerTransport(string certificateFile = "server.p12") =>
            new TcpServerTransport(authenticationOptions:
                new()
                {
                    ServerCertificate = new X509Certificate2(
                        Path.Combine(Environment.CurrentDirectory, "certs", certificateFile),
                        "password")
                });

        public static Endpoint GetTestEndpoint(
             string host = "127.0.0.1",
             int port = 0,
             string transport = "tcp",
             bool tls = false,
             ProtocolCode protocol = ProtocolCode.Ice2)
        {
            if (transport == "coloc" && host == "127.0.0.1" && port == 0)
            {
                return GetUniqueColocEndpoint(protocol);
            }

            if (protocol == ProtocolCode.Ice2)
            {
                string endpoint = $"ice+{transport}://{EscapeIPv6Address(host, protocol)}:{port}";
                if (transport == "tcp" && !tls)
                {
                    endpoint = $"{endpoint}?tls=false";
                }
                return endpoint;
            }
            else
            {
                return $"{transport} -h {EscapeIPv6Address(host, protocol)} -p {port}";
            }
        }

        public static string GetTestProxy(
            string path,
            string host = "127.0.0.1",
            int port = 0,
            string transport = "tcp",
            bool tls = false,
            ProtocolCode protocol = ProtocolCode.Ice2)
        {
            if (protocol == ProtocolCode.Ice2)
            {
                string proxy = $"ice+{transport}://{EscapeIPv6Address(host, protocol)}:{port}{path}";
                if (transport == "tcp" && !tls)
                {
                    proxy = $"{proxy}?tls=false";
                }
                return proxy;
            }
            else
            {
                return $"{path}:{transport} -h {EscapeIPv6Address(host, protocol)} -p {port}";
            }
        }

        public static Endpoint GetUniqueColocEndpoint(ProtocolCode protocol = ProtocolCode.Ice2) =>
            protocol == ProtocolCode.Ice2 ? $"ice+coloc://test.{Interlocked.Increment(ref _counter)}" :
                $"coloc -h test.{Interlocked.Increment(ref _counter)}";

        public static IServerTransport CreateServerTransport(
            Endpoint endpoint,
            object? options = null,
            object? multiStreamOptions = null,
            SslServerAuthenticationOptions? authenticationOptions = null) =>
            new LogServerTransportDecorator(endpoint.Transport switch
                {
                    "tcp" => new TcpServerTransport(
                        (TcpOptions?)options ?? new(),
                        (SlicOptions?)multiStreamOptions ?? new SlicOptions(),
                        authenticationOptions),
                    "ssl" => new TcpServerTransport(
                        (TcpOptions?)options ?? new(),
                        (SlicOptions?)multiStreamOptions ?? new SlicOptions(),
                        authenticationOptions),
                    "udp" => new UdpServerTransport((UdpOptions?)options ?? new()),
                    "coloc" => new ColocServerTransport((SlicOptions?)multiStreamOptions ?? new SlicOptions()),
                    _ => throw new UnknownTransportException(endpoint.Transport, endpoint.Protocol)
                });

        public static IClientTransport CreateClientTransport(
            Endpoint endpoint,
            object? options = null,
            object? multiStreamOptions = null,
            SslClientAuthenticationOptions? authenticationOptions = null) =>
            new LogClientTransportDecorator(endpoint.Transport switch
                {
                    "tcp" => new TcpClientTransport(
                        (TcpOptions?)options ?? new(),
                        (SlicOptions?)multiStreamOptions ?? new SlicOptions(),
                        authenticationOptions),
                    "ssl" => new TcpClientTransport(
                        (TcpOptions?)options ?? new(),
                        (SlicOptions?)multiStreamOptions ?? new SlicOptions(),
                        authenticationOptions),
                    "udp" => new UdpClientTransport((UdpOptions?)options ?? new()),
                    "coloc" => new ColocClientTransport((SlicOptions?)multiStreamOptions ?? new SlicOptions()),
                    _ => throw new UnknownTransportException(endpoint.Transport, endpoint.Protocol)
                });
    }
}
