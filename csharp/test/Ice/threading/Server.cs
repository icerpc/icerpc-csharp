// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using IceRpc.Test;

namespace IceRpc.Test.Threading
{
    public class ServerApp : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            await using var server = new Server(Communicator, new() { Endpoints = GetTestEndpoint(0) });
            server.Add("test", new TestIntf(TaskScheduler.Default));
            await server.ActivateAsync();

            var schedulerPair = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, 5);

            await using var server2 = new Server(
                Communicator,
                new() { Endpoints = GetTestEndpoint(1), TaskScheduler = schedulerPair.ExclusiveScheduler });
            server2.Add("test", new TestIntf(schedulerPair.ExclusiveScheduler));
            await server2.ActivateAsync();

            await using var server3 = new Server(
                Communicator,
                new() { Endpoints = GetTestEndpoint(2), TaskScheduler = schedulerPair.ConcurrentScheduler });
            server3.Add("test", new TestIntf(schedulerPair.ConcurrentScheduler));
            await server3.ActivateAsync();

            // Setup 20 worker threads for the .NET thread pool (we setup the minimum to avoid delays from the
            // thread pool thread creation).
            // TODO: Why are worker threads used here and not completion port threads? The SocketAsyncEventArgs
            // Completed event handler is called from the worker thread and not the completion port thread. This
            // might require fixing once we use Async socket primitives.
            ThreadPool.SetMinThreads(20, 4);
            ThreadPool.SetMaxThreads(20, 4);

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
