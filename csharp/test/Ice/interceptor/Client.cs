// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using IceRpc.Test;

namespace IceRpc.Test.Interceptor
{
    public class Client : TestHelper
    {
        public override Task RunAsync(string[] args) => AllTests.RunAsync(this);

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
                        "Ice.Plugin.InvocationPlugin",
                        $"{pluginPath}:IceRpc.Test.Interceptor.InvocationPluginFactory"
                    }
                });

            return await RunTestAsync<Client>(communicator, args);
        }
    }
}
