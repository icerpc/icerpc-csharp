// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.UDP
{
    public class ServerApp : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            Dictionary<string, string> properties = Communicator.GetProperties();
            int num = 0;
            try
            {
                num = args.Length == 1 ? int.Parse(args[0]) : 0;
            }
            catch (FormatException)
            {
            }

            var serverName = Communicator.GetProperty("Ice.ServerName") ?? "127.0.0.1";

            await using var server = new Server(
                Communicator,
                new()
                {
                    Endpoints = GetTestEndpoint(num, Transport),
                    ServerName = serverName
                });

            server.Add("control", new TestIntf());
            await server.ActivateAsync();
            ServerReady();
            if (num == 0)
            {
                var server2 = new Server(
                    Communicator,
                    new()
                    {
                        AcceptNonSecure = NonSecure.Always,
                        Endpoints = GetTestEndpoint(num, "udp"),
                        ServerName = serverName
                    });
                server2.Add("test", new TestIntf());
                await server2.ActivateAsync();
            }

            var endpoint = new StringBuilder();

            // Use loopback to prevent other machines to answer.
            if (Host.Contains(":"))
            {
                endpoint.Append("udp -h \"ff15::1:1\"");
                if (OperatingSystem.IsWindows() ||
                    OperatingSystem.IsMacOS())
                {
                    endpoint.Append(" --interface \"::1\"");
                }
            }
            else
            {
                endpoint.Append("udp -h 239.255.1.1");
                if (OperatingSystem.IsWindows() ||
                    OperatingSystem.IsMacOS())
                {
                    endpoint.Append(" --interface 127.0.0.1");
                }
            }
            endpoint.Append(" -p ");
            endpoint.Append(GetTestBasePort(properties) + 10);

            Server mcastAdapter = new Server(
                Communicator,
                new()
                {
                    AcceptNonSecure = NonSecure.Always,
                    Endpoints = endpoint.ToString(),
                    Name = "McastTestAdapter", // for test script ready check
                    ServerName = serverName
                });
            mcastAdapter.Add("test", new TestIntf());
            await mcastAdapter.ActivateAsync();

            ServerReady();
            await server.ShutdownComplete;
        }

        public static async Task<int> Main(string[] args)
        {
            Dictionary<string, string> properties = CreateTestProperties(ref args);
            properties["Ice.Warn.Connections"] = "0";
            properties["Ice.UDP.RcvSize"] = "16K";

            await using var communicator = CreateCommunicator(properties);
            return await RunTestAsync<ServerApp>(communicator, args);
        }
    }
}
