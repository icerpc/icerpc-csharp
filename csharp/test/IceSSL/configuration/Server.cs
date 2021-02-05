// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Threading.Tasks;
using ZeroC.Ice;
using ZeroC.Test;

namespace ZeroC.IceSSL.Test.Configuration
{
    public class Server : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            if (args.Length < 1)
            {
                throw new ArgumentException("Usage: server testdir");
            }

            await using var adapter = new ObjectAdapter(
                Communicator,
                "TestAdapter",
                new ObjectAdapterOptions
                {
                    AcceptNonSecure = NonSecure.Always,
                    ColocationScope = ColocationScope.Communicator,
                    Endpoints = GetTestEndpoint(0, "tcp"),
                    ServerName = TestHelper.GetTestHost(Communicator.GetProperties())
                });

            adapter.Add("factory", new ServerFactory(args[0] + "/../certs"));
            await adapter.ActivateAsync();

            ServerReady();
            await adapter.ShutdownComplete;
        }

        public static async Task<int> Main(string[] args)
        {
            await using var communicator = CreateCommunicator(ref args);
            return await RunTestAsync<Server>(communicator, args);
        }
    }
}
