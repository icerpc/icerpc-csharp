// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.ClientServer
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    public class UdpTests : ClientServerBaseTest
    {
        private Communicator ServerCommunicator { get; }
        public UdpTests() => ServerCommunicator = new Communicator();

        [TearDown]
        public async Task TearDown() =>
            await ServerCommunicator.DisposeAsync();

        [TestCase("127.0.0.1")]
        [TestCase("::1")]
        public async Task Upd_Request(string host)
        {
            await using var server = await SetupServerAsync(host, 0);

            await using var clientCoummunicator = new Communicator();

            IUdpServicePrx obj = IUdpServicePrx.Parse(GetTestProxy("test", host, transport: "udp", protocol: Protocol.Ice1),
              clientCoummunicator).Clone(oneway: true, preferNonSecure: NonSecure.Always);

            await PingAndWaitForReply(obj, host);
            // Disable dual mode sockets on macOS, see https://github.com/dotnet/corefx/issues/31182
            if (!OperatingSystem.IsMacOS() || !IsIPv6(host))
            {
                await PingBidirAndWaitForReply(obj, host);
            }
        }

        [TestCase("ff15::1:1", "::1")]
        [TestCase("239.255.1.1", "127.0.0.1")]
        public async Task Upd_MulticastRequest(string mcastAddress, string host)
        {
            // Disable dual mode sockets on macOS, see https://github.com/dotnet/corefx/issues/31182
            if (!OperatingSystem.IsMacOS() || !IsIPv6(host))
            {
                await using var server1 = await SetupMulticastServerAsync(mcastAddress, host);
                await using var server2 = await SetupMulticastServerAsync(mcastAddress, host);
                await using var server3 = await SetupMulticastServerAsync(mcastAddress, host);

                await using var clientCoummunicator = new Communicator();

                var str = $"test -d:udp -h {EscapeIPv6Address(mcastAddress, Protocol.Ice1)} -p {GetTestPort(2)}";
                if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
                {
                    str += $" --interface {EscapeIPv6Address(host, Protocol.Ice1)}";
                }
                var obj = IUdpServicePrx.Parse(str, clientCoummunicator).Clone(preferNonSecure: NonSecure.Always);

                await PingAndWaitForReply(obj, host);
            }
        }

        [TestCase(65535)]
        [TestCase(8192)]
        public async Task Upd_MaxDatagramSize(int size)
        {
            await using var server = await SetupServerAsync("127.0.0.1", 0);

            await using var clientCoummunicator = new Communicator();

            IUdpServicePrx obj = IUdpServicePrx.Parse(GetTestProxy("test", "127.0.0.1", transport: "udp", protocol: Protocol.Ice1),
              clientCoummunicator).Clone(oneway: true, preferNonSecure: NonSecure.Always);

            var replyService = new PingReply(1);

            await using var replyServer = new Server(
                obj.Communicator,
                new ServerOptions()
                {
                    AcceptNonSecure = NonSecure.Always,
                    Endpoints = GetTestEndpoint(host: "127.0.0.1", port: 1, transport: "udp", protocol: Protocol.Ice1),
                    PublishedHost = "127.0.0.1"
                });
            IUdpPingReplyPrx reply = replyServer.Add(Guid.NewGuid().ToString(), replyService, IUdpPingReplyPrx.Factory)
                .Clone(oneway: true, preferNonSecure: NonSecure.Always);
            await replyServer.ActivateAsync();

            const int maxDatagramSize = 65535;
            if (size >= maxDatagramSize)
            {
                Assert.ThrowsAsync<TransportException>(async () => await obj.SendByteSeqAsync(new byte[size], reply));
            }
            else
            {
                await obj.SendByteSeqAsync(new byte[size], reply);
                using var cancelation = new CancellationTokenSource(2000);
                await Task.WhenAny(replyService.Completed, Task.Delay(-1, cancelation.Token));
                Assert.IsTrue(replyService.Completed.IsCompleted);
            }
        }

        private async Task PingAndWaitForReply(IUdpServicePrx obj, string host)
        {
            var replyService = new PingReply(3);

            await using var replyServer = new Server(
                obj.Communicator,
                new ServerOptions()
                {
                    AcceptNonSecure = NonSecure.Always,
                    Endpoints = GetTestEndpoint(host: host, port: 1, transport: "udp", protocol: Protocol.Ice1),
                    PublishedHost = host
                });
            IUdpPingReplyPrx reply = replyServer.Add(Guid.NewGuid().ToString(), replyService, IUdpPingReplyPrx.Factory)
                .Clone(oneway: true, preferNonSecure: NonSecure.Always);
            await replyServer.ActivateAsync();

            for (int i = 0; i < 5; ++i)
            {
                await obj.PingAsync(reply);
                await obj.PingAsync(reply);
                await obj.PingAsync(reply);
                using var cancelation = new CancellationTokenSource(2000);

                await Task.WhenAny(replyService.Completed, Task.Delay(-1, cancelation.Token));
                if (replyService.Completed.IsCompleted)
                {
                    break; // Success
                }

                // If the 3 datagrams were not received within the 2 seconds, we try again to
                // receive 3 new datagrams using a new object. We give up after 5 retries.
                replyService = new PingReply(3);
                reply = replyServer.Add(Guid.NewGuid().ToString(), replyService, IUdpPingReplyPrx.Factory)
                    .Clone(oneway: true, preferNonSecure: NonSecure.Always);
            }

            if (!replyService.Completed.IsCompleted)
            {
                Assert.Fail($"no replies after 5 retries");
            }
        }

        private async Task PingBidirAndWaitForReply(IUdpServicePrx obj, string host)
        {
            var replyService = new PingReply(3);

            await using var replyServer = new Server(
                obj.Communicator,
                new ServerOptions()
                {
                    AcceptNonSecure = NonSecure.Always,
                    Endpoints = GetTestEndpoint(host: host, port: 1, transport: "udp", protocol: Protocol.Ice1),
                    PublishedHost = host
                });
            IUdpPingReplyPrx reply = replyServer.Add(Guid.NewGuid().ToString(), replyService, IUdpPingReplyPrx.Factory)
                .Clone(oneway: true, preferNonSecure: NonSecure.Always);
            await replyServer.ActivateAsync();
            (await obj.GetConnectionAsync()).Server = replyServer;
            for (int i = 0; i < 5; ++i)
            {
                await obj.PingBiDirAsync(reply.Path);
                await obj.PingBiDirAsync(reply.Path);
                await obj.PingBiDirAsync(reply.Path);
                using var cancelation = new CancellationTokenSource(2000);

                await Task.WhenAny(replyService.Completed, Task.Delay(-1, cancelation.Token));
                if (replyService.Completed.IsCompleted)
                {
                    break; // Success
                }

                // If the 3 datagrams were not received within the 2 seconds, we try again to
                // receive 3 new datagrams using a new object. We give up after 5 retries.
                replyService = new PingReply(3);
                reply = replyServer.Add(Guid.NewGuid().ToString(), replyService, IUdpPingReplyPrx.Factory)
                    .Clone(oneway: true, preferNonSecure: NonSecure.Always);
            }

            if (!replyService.Completed.IsCompleted)
            {
                Assert.Fail($"no replies after 5 retries");
            }
        }

        private async Task<Server> SetupServerAsync(string host, int port)
        {
            var server = new Server(
                ServerCommunicator,
                new()
                {
                    AcceptNonSecure = NonSecure.Always,
                    Endpoints = GetTestEndpoint(host, port, transport: "udp", protocol: Protocol.Ice1),
                    PublishedHost = host
                });
            server.Add("test", new UdpService());
            await server.ActivateAsync();
            return server;
        }

        public async Task<Server> SetupMulticastServerAsync(string mcastAddress, string host)
        {
            string endpoint = $"udp -h {EscapeIPv6Address(mcastAddress, Protocol.Ice1)} -p {GetTestPort(2)}";
            if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            {
                endpoint += $" --interface {EscapeIPv6Address(host, Protocol.Ice1)}";
            }

            var server = new Server(
                ServerCommunicator,
                new ServerOptions()
                {
                    AcceptNonSecure = NonSecure.Always,
                    Endpoints = endpoint.ToString(),
                    Name = "McastTestAdapter", // for test script ready check
                    PublishedHost = host
                });
            server.Add("test", new UdpService());
            await server.ActivateAsync();
            return server;
        }

        public class PingReply : IAsyncUdpPingReply
        {
            public Task Completed => _source.Task;

            private int _count;
            private readonly object _mutex;
            private readonly TaskCompletionSource _source;

            public PingReply(int count)
            {
                _count = count;
                _source = new TaskCompletionSource();
                _mutex = new();
            }

            public ValueTask ReplyAsync(Current current, CancellationToken cancel)
            {
                lock (_mutex)
                {
                    if (--_count == 0)
                    {
                        _source.SetResult();
                    }
                }
                return default;
            }
        }

        class UdpService : IAsyncUdpService
        {
            public async ValueTask PingAsync(IUdpPingReplyPrx reply, Current current, CancellationToken cancel) =>
                await reply.Clone(preferNonSecure: NonSecure.Always).ReplyAsync(cancel: cancel);

            public async ValueTask PingBiDirAsync(string path, Current current, CancellationToken cancel)
            {
                // Ensure sending too much data doesn't cause the UDP connection to be closed.
                try
                {
                    byte[] seq = new byte[64 * 1024];
                    IUdpServicePrx.Factory.Create(current.Connection, path).SendByteSeq(seq, null, cancel: cancel);
                }
                catch (TransportException)
                {
                    // Expected.
                }

                await IUdpPingReplyPrx.Factory.Create(current.Connection!, path).ReplyAsync(cancel: cancel);
            }

            public async ValueTask SendByteSeqAsync(byte[] seq, IUdpPingReplyPrx? reply, Current current, CancellationToken cancel)
            {
                if (reply != null)
                {
                    await reply.Clone(preferNonSecure: NonSecure.Always).ReplyAsync(cancel: cancel);
                }
            }
        }
    }
}
