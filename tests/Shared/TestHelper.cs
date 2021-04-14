// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace IceRpc.Tests
{
    public static class TestHelper
    {
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
            string identity,
            string host = "127.0.0.1",
            int port = 0,
            string transport = "tcp",
            Protocol protocol = Protocol.Ice2) =>
            protocol == Protocol.Ice2 ?
                $"ice+{transport}://{EscapeIPv6Address(host, protocol)}:{port}/{identity}" :
                $"{identity}:{transport} -h {EscapeIPv6Address(host, protocol)} -p {port}";
    }
}
