// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Threading.Tasks;
using IceRpc.Test;

namespace IceRpc.Test.Discovery
{
    public class Client : TestHelper
    {
        public override Task RunAsync(string[] args)
        {
            int num;
            try
            {
                num = args.Length == 1 ? int.Parse(args[0]) : 0;
            }
            catch (FormatException)
            {
                num = 0;
            }

            return AllTests.RunAsync(this, num);
        }

        public static async Task<int> Main(string[] args)
        {
            await using var communicator = CreateCommunicator(ref args);
            return await RunTestAsync<Client>(communicator, args);
        }
    }
}
