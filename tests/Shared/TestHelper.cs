// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Threading;
namespace IceRpc.Tests
{
    public static class TestHelper
    {
        private static ulong _counter;

        public static string EscapeIPv6Address(string address, Protocol protocol) =>
            protocol switch
            {
                Protocol.Ice1 => address.Contains(":") ? $"\"{address}\"" : address,
                _ => address.Contains(":") ? $"[{address}]" : address
            };

        public static string GetTestEndpoint(
            string host = "127.0.0.1",
            int port = 0,
            string transport = "tcp",
            Protocol protocol = Protocol.Ice2) =>
            protocol == Protocol.Ice2 ?
                $"ice+{transport}://{EscapeIPv6Address(host, protocol)}:{port}" :
                $"{transport} -h {EscapeIPv6Address(host, protocol)} -p {port}";

        public static string GetTestProxy(
            string path,
            string host = "127.0.0.1",
            int port = 0,
            string transport = "tcp",
            Protocol protocol = Protocol.Ice2) =>
            protocol == Protocol.Ice2 ?
                $"ice+{transport}://{EscapeIPv6Address(host, protocol)}:{port}{path}" :
                $"{path}:{transport} -h {EscapeIPv6Address(host, protocol)} -p {port}";

        public static string GetUniqueTestServerColocEndpoint(Protocol protocol = Protocol.Ice2) =>
            protocol == Protocol.Ice2 ? $"ice+coloc://test.{Interlocked.Increment(ref _counter)}" :
                $"coloc -h test.{Interlocked.Increment(ref _counter)}";
    }
}
