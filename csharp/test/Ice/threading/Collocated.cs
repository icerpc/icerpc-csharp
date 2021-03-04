// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using IceRpc.Test;

namespace IceRpc.Test.Threading
{
    public class Collocated : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            await using var server1 = new Server(Communicator, new() { Endpoints = GetTestEndpoint(0) });
            server1.Add("test", new TestIntf(TaskScheduler.Default));

            var schedulerPair = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, 5);

            await using var server2 = new Server(
                Communicator,
                new() { Endpoints = GetTestEndpoint(1), TaskScheduler = schedulerPair.ExclusiveScheduler });
            server2.Add("test", new TestIntf(schedulerPair.ExclusiveScheduler));

            await using var server3 = new Server(
                Communicator,
                new() { Endpoints = GetTestEndpoint(2), TaskScheduler = schedulerPair.ConcurrentScheduler });
            server3.Add("test", new TestIntf(schedulerPair.ConcurrentScheduler));

            // Setup 21 worker threads for the .NET thread pool (we setup the minimum to avoid delays from the
            // thread pool thread creation). Unlike the server we setup one additional thread for running the
            // allTests task in addition to the 20 concurrent threads which are needed for concurrency testing.
            // TODO: Why are worker threads used here and not completion port threads? The SocketAsyncEventArgs
            // Completed event handler is called from the worker thread and not the completion port thread.
            ThreadPool.SetMinThreads(21, 4);
            ThreadPool.SetMaxThreads(21, 4);

            try
            {
                await AllTests.RunAsync(this, true);
            }
            catch (TestFailedException ex)
            {
                Output.WriteLine($"test failed: {ex.Reason}");
                Assert(false);
                throw;
            }
        }

        public static async Task<int> Main(string[] args)
        {
            await using var communicator = CreateCommunicator(ref args);
            return await RunTestAsync<Collocated>(communicator, args);
        }
    }
}
