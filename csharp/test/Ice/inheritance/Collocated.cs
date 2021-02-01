// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.Inheritance
{
    public class Collocated : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            ObjectAdapter adapter = Communicator.CreateObjectAdapter(
                "TestAdapter",
                new ObjectAdapterOptions { Endpoints = GetTestEndpoint(0) });

            adapter.Add("initial", new InitialI(adapter));

            await AllTests.RunAsync(this);
        }

        public static async Task<int> Main(string[] args)
        {
            await using var communicator = CreateCommunicator(ref args);
            return await RunTestAsync<Collocated>(communicator, args);
        }
    }
}
