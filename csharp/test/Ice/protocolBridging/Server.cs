// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Generic;
using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.ProtocolBridging
{
    public class Server : TestHelper
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

            ObjectAdapter adapterForwarder = Communicator.CreateObjectAdapter(
                "TestAdapterForwarder",
                new ObjectAdapterOptions { Endpoints = GetTestEndpoint(0) });

            ObjectAdapter adapterSame = Communicator.CreateObjectAdapter(
                "TestAdapterSame",
                new ObjectAdapterOptions { Endpoints = ice1 ? ice1Endpoint : ice2Endpoint });

            ObjectAdapter adapterOther = Communicator.CreateObjectAdapter(
                "TestAdapterOther",
                new ObjectAdapterOptions { Endpoints = ice1 ? ice2Endpoint : ice1Endpoint });

            ITestIntfPrx samePrx = adapterSame.Add("TestSame", new TestI(), ITestIntfPrx.Factory);
            ITestIntfPrx otherPrx = adapterOther.Add("TestOther", new TestI(), ITestIntfPrx.Factory);

            adapterForwarder.Add("ForwardSame", new Forwarder(samePrx));
            adapterForwarder.Add("ForwardOther", new Forwarder(otherPrx));

            await adapterForwarder.ActivateAsync();
            await adapterSame.ActivateAsync();
            await adapterOther.ActivateAsync();

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
