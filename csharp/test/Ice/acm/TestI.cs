// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZeroC.Test;

namespace ZeroC.Ice.Test.ACM
{
    public class RemoteCommunicator : IAsyncRemoteCommunicator
    {
        public async ValueTask<IRemoteServerPrx> CreateServerAsync(
            int idleTimeout,
            bool keepAlive,
            Current current,
            CancellationToken cancel)
        {
            var communicator = new Communicator(
                new Dictionary<string, string>(current.Communicator.GetProperties())
                {
                    ["Ice.Warn.Connections"] = "0",
                    ["Ice.IdleTimeout"] = $"{idleTimeout}s",
                    ["Ice.KeepAlive"] = keepAlive ? "1" : "0"
                });

            var schedulerPair = new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default);
            string endpoint = TestHelper.GetTestEndpoint(properties: communicator.GetProperties(), ephemeral: true);

            Server server = new Server(
                communicator,
                new()
                {
                    Endpoints = endpoint,
                    TaskScheduler = schedulerPair.ExclusiveScheduler
                });

            await server.ActivateAsync(cancel);
            return current.Server.AddWithUUID(new RemoteServer(server), IRemoteServerPrx.Factory);
        }

        public ValueTask ShutdownAsync(Current current, CancellationToken cancel)
        {
            _ = current.Server.ShutdownAsync();
            return default;
        }
    }

    public class RemoteServer : IRemoteServer
    {
        private readonly Server _server;
        private readonly ITestIntfPrx _testIntf;

        public RemoteServer(Server server)
        {
            _server = server;
            _testIntf = _server.Add("test", new TestIntf(), ITestIntfPrx.Factory);
        }

        public ITestIntfPrx GetTestIntf(Current current, CancellationToken cancel) => _testIntf;

        public void Deactivate(Current current, CancellationToken cancel) =>
            _server.ShutdownAsync();
    }

    public class TestIntf : ITestIntf
    {
        private int _count;
        private readonly object _mutex = new();

        public void Sleep(int delay, Current current, CancellationToken cancel)
        {
            Thread.Sleep(TimeSpan.FromSeconds(delay));
        }

        public void StartHeartbeatCount(Current current, CancellationToken cancel)
        {
            _count = 0;
            current.Connection.PingReceived += (sender, args) =>
            {
                lock (_mutex)
                {
                    ++_count;
                    Monitor.PulseAll(_mutex);
                }
            };
        }

        public void WaitForHeartbeatCount(int count, Current current, CancellationToken cancel)
        {
            lock (_mutex)
            {
                while (_count < count)
                {
                    Monitor.Wait(_mutex);
                }
            }
        }
    }
}
