// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading.Tasks;
using ZeroC.Ice;
using ZeroC.Test;

namespace ZeroC.Ice.Test.Info
{
    public class ServerApp : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            ServerOptions options;

            if (Protocol == Protocol.Ice1)
            {
                options = new ServerOptions
                {
                    AcceptNonSecure = NonSecure.Always,
                    Endpoints = GetTestEndpoint(0) + ":" + GetTestEndpoint(0, "udp"),
                    Name = "TestAdapter"
                };
            }
            else
            {
                options = new ServerOptions { Endpoints = GetTestEndpoint(0), Name = "TestAdapter" };
            }

            await using var server = new Server(Communicator, options);
            server.Add("test", new TestIntf());
            await server.ActivateAsync();

            ServerReady();
            await server.ShutdownComplete;
        }

        public static async Task<int> Main(string[] args)
        {
            await using var communicator = CreateCommunicator(ref args);
            return await RunTestAsync<ServerApp>(communicator, args);
        }
    }
}
