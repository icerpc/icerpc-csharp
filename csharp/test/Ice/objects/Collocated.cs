// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.Objects
{
    public class Collocated : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            await using var server = new Server(Communicator,
                                                        new() { Endpoints = GetTestEndpoint(0) });

            server.Add("initial", new Initial(server));
            server.Add("F21", new F2());
            var uoet = new UnexpectedObjectExceptionTest();
            server.Add("uoet", uoet);

            await AllTests.RunAsync(this);
        }

        public static async Task<int> Main(string[] args)
        {
            var properties = CreateTestProperties(ref args);
            properties["Ice.Warn.Dispatch"] = "0";

            await using var communicator = CreateCommunicator(properties);
            return await RunTestAsync<Collocated>(communicator, args);
        }
    }
}
