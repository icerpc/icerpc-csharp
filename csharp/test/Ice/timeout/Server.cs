// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.Timeout
{
    public class ServerApp : TestHelper
    {
        public override async Task RunAsync(string[] args)
        {
            var schedulerPair = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default);
            await using var adapter = new Server(Communicator,
                                                        new()
                                                        {
                                                            Endpoints = GetTestEndpoint(0),
                                                            TaskScheduler = schedulerPair.ExclusiveScheduler
                                                        });

            adapter.Add("timeout", new Timeout());
            adapter.Use(
                (request, current, next, cancel) =>
                {
                    if (current.Operation == "checkDeadline")
                    {
                        if (request.BinaryContext.TryGetValue(10, out ReadOnlyMemory<byte> value))
                        {
                            current.Context["deadline"] = value.Read(istr => istr.ReadVarLong()).ToString();
                        }
                    }
                    return next();
                });

            await adapter.ActivateAsync();

            await using var controllerAdapter = new Server(
                Communicator,
                new() { Endpoints = GetTestEndpoint(1) });
            controllerAdapter.Add("controller", new Controller(schedulerPair.ExclusiveScheduler));
            await controllerAdapter.ActivateAsync();

            ServerReady();
            await controllerAdapter.ShutdownComplete;
        }

        public static async Task<int> Main(string[] args)
        {
            Dictionary<string, string> properties = CreateTestProperties(ref args);
            // This test kills connections, so we don't want warnings.
            properties["Ice.Warn.Connections"] = "0";
            // The client sends large messages to cause the transport buffers to fill up.
            properties["Ice.IncomingFrameMaxSize"] = "20M";
            // Limit the recv buffer size, this test relies on the socket send() blocking after sending a given
            // amount of data.
            properties["Ice.TCP.RcvSize"] = "50K";

            await using var communicator = CreateCommunicator(properties);
            return await RunTestAsync<ServerApp>(communicator, args);
        }
    }

    internal class Controller : IController
    {
        private readonly TaskScheduler _scheduler;
        private readonly SemaphoreSlim _semaphore = new(0);

        public Controller(TaskScheduler scheduler) => _scheduler = scheduler;

        public void HoldAdapter(int to, Current current, CancellationToken cancel)
        {
            Task.Factory.StartNew(() => _semaphore.Wait(), default, TaskCreationOptions.None, _scheduler);
            if (to >= 0)
            {
                Task.Delay(to, cancel).ContinueWith(t => _semaphore.Release(), TaskScheduler.Default);
            }
        }

        public void ResumeAdapter(Current current, CancellationToken cancel) => _ = _semaphore.Release();

        public void Shutdown(Current current, CancellationToken cancel) =>
            current.Server.ShutdownAsync();
    }
}
