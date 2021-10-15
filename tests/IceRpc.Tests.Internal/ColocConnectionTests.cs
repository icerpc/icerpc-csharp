// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using NUnit.Framework;
using System.Threading.Channels;

namespace IceRpc.Tests.Internal
{
    public class ColocNetworkConnectionTests
    {
        [Test]
        public void ColocNetworkConnection_Close()
        {
            ColocNetworkConnection connection = CreateConnection(false);
            connection.Close();
            connection.Close();
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        public void ColocNetworkConnection_HasCompatibleParams(bool isServer, bool expectedResult)
        {
            ColocNetworkConnection connection = CreateConnection(isServer);
            Assert.That(connection.HasCompatibleParams(Endpoint.FromString("ice+coloc://host")),
                        Is.EqualTo(expectedResult));
            connection.Close();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ColocNetworkConnection_Properties(bool isServer)
        {
            ColocNetworkConnection connection = CreateConnection(isServer);

            (_, NetworkConnectionInformation information) = await connection.ConnectAndGetNetworkStreamAsync(default);

            Assert.That(information.LocalEndpoint, Is.EqualTo(Endpoint.FromString("ice+coloc://host")));
            Assert.That(information.RemoteEndpoint, Is.EqualTo(Endpoint.FromString("ice+coloc://host")));
            Assert.That(information.IdleTimeout, Is.EqualTo(TimeSpan.MaxValue));
            Assert.That(connection.IsDatagram, Is.False);

            connection.Close();
        }

        [Test]
        public async Task ColocNetworkConnection_LastActivity()
        {
            ColocNetworkConnection connection = CreateConnection(false);

            (INetworkStream stream, _) = await connection.ConnectAndGetNetworkStreamAsync(default);

            // Coloc connections are not closed by ACM.
            // TODO: should they?

            await Task.Delay(2);
            await stream.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[1] }, default);
            Assert.That(connection.LastActivity, Is.EqualTo(TimeSpan.Zero));

            await Task.Delay(2);
            await stream.ReadAsync(new byte[1], default);
            Assert.That(connection.LastActivity, Is.EqualTo(TimeSpan.Zero));

            connection.Close();
        }

        private static ColocNetworkConnection CreateConnection(bool isServer)
        {
            var channel = Channel.CreateUnbounded<ReadOnlyMemory<byte>>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = false
                });

            return new ColocNetworkConnection(
                Endpoint.FromString("ice+coloc://host"),
                isServer: isServer,
                writer: channel.Writer,
                reader: channel.Reader);
        }
    }
}
