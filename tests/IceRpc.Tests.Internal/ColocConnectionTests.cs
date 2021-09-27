// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using NUnit.Framework;
using System.Threading.Channels;

namespace IceRpc.Tests.Internal
{
    public class ColocConnectionTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void ColocConnection_ConnectAsync(bool isServer)
        {
            using ColocConnection connection = CreateConnection(isServer);
            Assert.DoesNotThrowAsync(async () => await connection.ConnectAsync(default));
        }

        [Test]
        public void ColocConnection_Dispose()
        {
            using ColocConnection connection = CreateConnection(false);
            connection.Dispose();
            connection.Dispose();
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        public async Task ColocConnection_HasCompatibleParams(bool isServer, bool expectedResult)
        {
            using ColocConnection connection = CreateConnection(isServer);
            await connection.ConnectAsync(default);
            Assert.That(connection.HasCompatibleParams(Endpoint.FromString("ice+coloc://host")),
                        Is.EqualTo(expectedResult));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ColocConnection_Properties(bool isServer)
        {
            using ColocConnection connection = CreateConnection(isServer);

            Assert.That(connection.LocalEndpoint, Is.EqualTo(Endpoint.FromString("ice+coloc://host")));
            Assert.That(connection.RemoteEndpoint, Is.EqualTo(Endpoint.FromString("ice+coloc://host")));
            Assert.That(connection.IsServer, Is.EqualTo(isServer));
            Assert.That(connection.IdleTimeout, Is.EqualTo(TimeSpan.MaxValue));
            Assert.That(connection.IsDatagram, Is.False);
        }

        [Test]
        public async Task ColocConnection_LastActivity()
        {
            using ColocConnection connection = CreateConnection(false);

            ISingleStreamConnection stream = await connection.GetSingleStreamConnectionAsync(default);

            // Coloc connections are not closed by ACM.
            // TODO: should they?

            await Task.Delay(2);
            await stream.SendAsync(new ReadOnlyMemory<byte>[] { new byte[1] }, default);
            Assert.That(connection.LastActivity, Is.EqualTo(TimeSpan.FromSeconds(0)));

            await Task.Delay(2);
            await stream.SendAsync(new byte[1], default);
            Assert.That(connection.LastActivity, Is.EqualTo(TimeSpan.FromSeconds(0)));

            await Task.Delay(2);
            await stream.ReceiveAsync(new byte[1], default);
            Assert.That(connection.LastActivity, Is.EqualTo(TimeSpan.FromSeconds(0)));
        }

        private ColocConnection CreateConnection(bool isServer)
        {
            var channel = Channel.CreateUnbounded<ReadOnlyMemory<byte>>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                });

            return new ColocConnection(
                Endpoint.FromString("ice+coloc://host"),
                isServer: isServer,
                slicOptions: new(),
                writer: channel.Writer,
                reader: channel.Reader,
                logger: null!);
        }
    }
}
