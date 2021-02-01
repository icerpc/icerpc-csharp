// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading.Tasks;
using ZeroC.Ice;
using ZeroC.Test;

namespace ZeroC.Ice.Test.Info
{
    public class Server : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            ObjectAdapterOptions options;

            if (Protocol == Protocol.Ice1)
            {
                options = new ObjectAdapterOptions
                {
                    Endpoints = GetTestEndpoint(0) + ":" + GetTestEndpoint(0, "udp"),
                    AcceptNonSecure = NonSecure.Always
                };
            }
            else
            {
                options = new ObjectAdapterOptions { Endpoints = GetTestEndpoint(0) };
            }

            ObjectAdapter adapter = Communicator.CreateObjectAdapter("TestAdapter", options);
            adapter.Add("test", new TestIntf());
            await adapter.ActivateAsync();

            ServerReady();
            await Communicator.ShutdownComplete;
        }

        public static async Task<int> Main(string[] args)
        {
            await using var communicator = CreateCommunicator(ref args);
            return await RunTestAsync<Server>(communicator, args);
        }
    }
}
