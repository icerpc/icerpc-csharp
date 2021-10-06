// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.Threading.Channels;

namespace IceRpc.Tests.Internal
{
    public class ColocConnectionTests
    {
        [Test]
        public void ColocConnection_Close()
        {
            ColocConnection connection = CreateConnection(false);
            connection.Close();
            connection.Close();
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        public void ColocConnection_HasCompatibleParams(bool isServer, bool expectedResult)
        {
            ColocConnection connection = CreateConnection(isServer);
            Assert.That(connection.HasCompatibleParams(Endpoint.FromString("ice+coloc://host")),
                        Is.EqualTo(expectedResult));
            connection.Close();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ColocConnection_Properties(bool isServer)
        {
            ColocConnection connection = CreateConnection(isServer);

            Assert.That(connection.LocalEndpoint, Is.EqualTo(Endpoint.FromString("ice+coloc://host")));
            Assert.That(connection.RemoteEndpoint, Is.EqualTo(Endpoint.FromString("ice+coloc://host")));
            Assert.That(connection.IsServer, Is.EqualTo(isServer));
            Assert.That(connection.IdleTimeout, Is.EqualTo(TimeSpan.MaxValue));
            Assert.That(connection.IsDatagram, Is.False);

            connection.Close();
        }

        [Test]
        public async Task ColocConnection_LastActivity()
        {
            ColocConnection connection = CreateConnection(false);

            ISingleStreamConnection stream = await connection.GetSingleStreamConnectionAsync(default);

            // Coloc connections are not closed by ACM.
            // TODO: should they?

            await Task.Delay(2);
            await stream.SendAsync(new ReadOnlyMemory<byte>[] { new byte[1] }, default);
            Assert.That(connection.LastActivity, Is.EqualTo(TimeSpan.Zero));

            await Task.Delay(2);
            await stream.ReceiveAsync(new byte[1], default);
            Assert.That(connection.LastActivity, Is.EqualTo(TimeSpan.Zero));

            connection.Close();
        }

        private ColocConnection CreateConnection(bool isServer)
        {
            var channel = Channel.CreateUnbounded<ReadOnlyMemory<byte>>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = false
                });

            return new ColocConnection(
                Endpoint.FromString("ice+coloc://host"),
                isServer: isServer,
                slicOptions: new(),
                writer: channel.Writer,
                reader: channel.Reader,
                logger: NullLogger.Instance);
        }
    }
}
