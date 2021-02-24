// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Threading.Tasks;
using ZeroC.Ice;
using ZeroC.Test;

namespace ZeroC.IceSSL.Test.Configuration
{
    public class ServerApp : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            if (args.Length < 1)
            {
                throw new ArgumentException("Usage: server testdir");
            }

            await using var server = new Server(
                Communicator,
                new()
                {
                    AcceptNonSecure = NonSecure.Always,
                    ColocationScope = ColocationScope.Communicator,
                    Endpoints = GetTestEndpoint(0, "tcp"),
                    ServerName = TestHelper.GetTestHost(Communicator.GetProperties())
                });

            server.Add("factory", new ServerFactory(args[0] + "/../certs"));
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
