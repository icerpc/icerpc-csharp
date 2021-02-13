// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.FaultTolerance
{
    public class Server : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            int port = 0;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i][0] == '-')
                {
                    throw new ArgumentException("Server: unknown option `" + args[i] + "'");
                }

                if (port != 0)
                {
                    throw new ArgumentException("Server: only one port can be specified");
                }

                try
                {
                    port = int.Parse(args[i]);
                }
                catch (FormatException)
                {
                    throw new ArgumentException("Server: invalid port");
                }
            }

            if (port <= 0)
            {
                throw new ArgumentException("Server: no port specified");
            }

            await using var adapter = new ObjectAdapter(Communicator, new() { Endpoints = GetTestEndpoint(port) });

            adapter.Add("test", new TestIntf());
            await adapter.ActivateAsync();

            ServerReady();
            await adapter.ShutdownComplete;
        }

        public static async Task<int> Main(string[] args)
        {
            Dictionary<string, string> properties = CreateTestProperties(ref args);
            properties["Ice.ServerIdleTime"] = "120";

            await using var communicator = CreateCommunicator(properties);
            return await RunTestAsync<Server>(communicator, args);
        }
    }
}
