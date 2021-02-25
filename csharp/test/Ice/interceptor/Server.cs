// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.Interceptor
{
    public class ServerApp : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            await using var server = new Server(Communicator,
                                                        new() { Endpoints = GetTestEndpoint(0) });

            server.Add("test", new MyObject());
            await DispatchInterceptors.ActivateAsync(server);

            ServerReady();
            await server.ShutdownComplete;
        }

        public static async Task<int> Main(string[] args)
        {
            string pluginPath =
                string.Format("msbuild/plugin/{0}/Plugin.dll",
                    Path.GetFileName(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)));

            await using var communicator = CreateCommunicator(
                ref args,
                new Dictionary<string, string>()
                {
                    {
                        "Ice.Plugin.DispatchPlugin",
                        $"{pluginPath}:ZeroC.Ice.Test.Interceptor.DispatchPluginFactory"
                    }
                });

            return await RunTestAsync<ServerApp>(communicator, args);
        }
    }
}
