// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.ProtocolBridging
{
    public class ServerApp : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            var ice1Endpoint = TestHelper.GetTestEndpoint(
                new Dictionary<string, string>
                {
                    ["Test.Host"] = Communicator.GetProperty("Test.Host")!,
                    ["Test.Protocol"] = "ice1",
                    ["Test.Transport"] = Communicator.GetProperty("Test.Transport")!,
                },
                ephemeral: true);

            var ice2Endpoint = TestHelper.GetTestEndpoint(
                new Dictionary<string, string>
                {
                    ["Test.Host"] = Communicator.GetProperty("Test.Host")!,
                    ["Test.Protocol"] = "ice2",
                    ["Test.Transport"] = Communicator.GetProperty("Test.Transport")!,
                },
                ephemeral: true);

            bool ice1 = Protocol == Protocol.Ice1;

            await using var serverForwarder =
                new Server(Communicator, new() { Endpoints = GetTestEndpoint(0) });

            await using var serverSame =
                new Server(Communicator, new() { Endpoints = ice1 ? ice1Endpoint : ice2Endpoint });

            await using var serverOther =
                new Server(Communicator, new() { Endpoints = ice1 ? ice2Endpoint : ice1Endpoint });

            ITestIntfPrx samePrx = serverSame.Add("TestSame", new TestI(), ITestIntfPrx.Factory);
            ITestIntfPrx otherPrx = serverOther.Add("TestOther", new TestI(), ITestIntfPrx.Factory);

            serverForwarder.Add("ForwardSame", new Forwarder(samePrx));
            serverForwarder.Add("ForwardOther", new Forwarder(otherPrx));

            await serverForwarder.ActivateAsync();
            await serverSame.ActivateAsync();
            await serverOther.ActivateAsync();

            ServerReady();
            await Task.WhenAny(serverSame.ShutdownComplete, serverOther.ShutdownComplete);
        }

        public static async Task<int> Main(string[] args)
        {
            await using var communicator = CreateCommunicator(ref args);
            return await RunTestAsync<ServerApp>(communicator, args);
        }
    }
}
