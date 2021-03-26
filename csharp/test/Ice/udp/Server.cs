// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using IceRpc.Test;

namespace IceRpc.Test.UDP
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

            var publishedHost = Communicator.GetProperty("Ice.PublishedHost") ?? "127.0.0.1";

            await using var server = new Server(
                Communicator,
                new()
                {
                    Endpoints = GetTestEndpoint(num, Transport),
                    PublishedHost = publishedHost
                });

            server.Add("control", new TestIntf());
            server.Activate();
            ServerReady();
            if (num == 0)
            {
                var server2 = new Server(
                    Communicator,
                    new()
                    {
                        ConnectionOptions = new()
                        {
                            AcceptNonSecure = NonSecure.Always
                        },
                        Endpoints = GetTestEndpoint(num, "udp"),
                        PublishedHost = publishedHost
                    });
                server2.Add("test", new TestIntf());
                server2.Activate();
            }

            var endpoint = new StringBuilder();

            // Use loopback to prevent other machines to answer.
            if (Host.Contains(":"))
            {
                endpoint.Append("udp -h \"ff02::1\"");
            }
            else
            {
                endpoint.Append("udp -h 239.255.1.1");
            }
            endpoint.Append(" -p ");
            endpoint.Append(GetTestBasePort(properties) + 10);
            Server mcastAdapter = new Server(
                Communicator,
                new()
                {
                    ConnectionOptions = new()
                    {
                        AcceptNonSecure = NonSecure.Always
                    },
                    Endpoints = endpoint.ToString(),
                    Name = "McastTestAdapter", // for test script ready check
                    PublishedHost = publishedHost
                });
            mcastAdapter.Add("test", new TestIntf());
            mcastAdapter.Activate();

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
