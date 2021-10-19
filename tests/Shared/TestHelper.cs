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

        public static IServerTransport CreateServerTransport(
            string transport = "tcp",
            object? options = null,
            object? multiStreamOptions = null,
            SslServerAuthenticationOptions? authenticationOptions = null,
            ILoggerFactory? loggerFactory = null)
        {
            return transport switch
                {
                    "tcp" => LogSimpleTransportDecorator(new TcpServerTransport(
                        (TcpOptions?)options ?? new(),
                        (SlicOptions?)multiStreamOptions ?? new SlicOptions(),
                        authenticationOptions)),
                    "ssl" => LogSimpleTransportDecorator(new TcpServerTransport(
                        (TcpOptions?)options ?? new(),
                        (SlicOptions?)multiStreamOptions ?? new SlicOptions(),
                        authenticationOptions)),
                    "udp" => LogUdpTransportDecorator(new UdpServerTransport((UdpOptions?)options ?? new())),
                    "coloc" => LogSimpleTransportDecorator(new ColocServerTransport(
                        (SlicOptions?)multiStreamOptions ?? new SlicOptions())),
                    _ => throw new UnknownTransportException(transport)
                };

            IServerTransport LogUdpTransportDecorator(UdpServerTransport transport) =>
                loggerFactory == null ? transport : transport.UseLoggerFactory(loggerFactory);

            IServerTransport LogSimpleTransportDecorator(SimpleServerTransport transport) =>
                loggerFactory == null ? transport : transport.UseLoggerFactory(loggerFactory);
        }

        public static IClientTransport CreateClientTransport(
            string transport = "tcp",
            object? options = null,
            object? multiStreamOptions = null,
            SslClientAuthenticationOptions? authenticationOptions = null,
            ILoggerFactory? loggerFactory = null)
        {
            return transport switch
                {
                    "tcp" => LogSimpleTransportDecorator(new TcpClientTransport(
                        (TcpOptions?)options ?? new(),
                        (SlicOptions?)multiStreamOptions ?? new SlicOptions(),
                        authenticationOptions)),
                    "ssl" => LogSimpleTransportDecorator(new TcpClientTransport(
                        (TcpOptions?)options ?? new(),
                        (SlicOptions?)multiStreamOptions ?? new SlicOptions(),
                        authenticationOptions)),
                    "udp" => LogUdpTransportDecorator(new UdpClientTransport((UdpOptions?)options ?? new())),
                    "coloc" => LogSimpleTransportDecorator(new ColocClientTransport(
                        (SlicOptions?)multiStreamOptions ?? new SlicOptions())),
                    _ => throw new UnknownTransportException(transport)
                };

            IClientTransport LogUdpTransportDecorator(UdpClientTransport transport) =>
                loggerFactory == null ? transport : transport.UseLoggerFactory(loggerFactory);

            IClientTransport LogSimpleTransportDecorator(SimpleClientTransport transport) =>
                loggerFactory == null ? transport : transport.UseLoggerFactory(loggerFactory);
        }
    }
}
