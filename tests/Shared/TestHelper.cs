// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Net.Security;

namespace IceRpc.Tests
{
    public static class TestHelper
    {
        private static ulong _counter;

        public static string EscapeIPv6Address(string address, Protocol protocol) =>
            protocol switch
            {
                Protocol.Ice1 => address.Contains(':', StringComparison.InvariantCulture) ? $"\"{address}\"" : address,
                _ => address.Contains(':', StringComparison.InvariantCulture) ? $"[{address}]" : address
            };

        public static Endpoint GetTestEndpoint(
             string host = "127.0.0.1",
             int port = 0,
             string transport = "tcp",
             bool tls = false,
             Protocol protocol = Protocol.Ice2)
        {
            if (protocol == Protocol.Ice2)
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
            Protocol protocol = Protocol.Ice2)
        {
            if (protocol == Protocol.Ice2)
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

        public static Endpoint GetUniqueColocEndpoint(Protocol protocol = Protocol.Ice2) =>
            protocol == Protocol.Ice2 ? $"ice+coloc://test.{Interlocked.Increment(ref _counter)}" :
                $"coloc -h test.{Interlocked.Increment(ref _counter)}";

        public static IServerTransport CreateServerTransport(
            Endpoint endpoint,
            object? options = null,
            MultiStreamOptions? multiStreamOptions = null,
            SslServerAuthenticationOptions? authenticationOptions = null) =>
            endpoint.Transport switch
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
                "coloc" => new ColocServerTransport(multiStreamOptions ?? new()),
                _ => throw new UnknownTransportException(endpoint.Transport, endpoint.Protocol)
            };

        public static IClientTransport CreateClientTransport(
            Endpoint endpoint,
            object? options = null,
            MultiStreamOptions? multiStreamOptions = null,
            SslClientAuthenticationOptions? authenticationOptions = null) =>
            endpoint.Transport switch
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
                "coloc" => new ColocClientTransport(multiStreamOptions ?? new()),
                _ => throw new UnknownTransportException(endpoint.Transport, endpoint.Protocol)
            };
    }
}
