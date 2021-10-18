// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using NUnit.Framework;
using System.Threading.Channels;

namespace IceRpc.Tests.Internal
{
    public class ColocConnectionTests
    {
        [Test]
        public void ColocConnection_Close()
        {
            ColocNetworkConnection connection = CreateConnection(false);
            connection.Close();
            connection.Close();
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        public void ColocConnection_HasCompatibleParams(bool isServer, bool expectedResult)
        {
            ColocNetworkConnection connection = CreateConnection(isServer);
            Assert.That(connection.HasCompatibleParams(Endpoint.FromString("ice+coloc://host")),
                        Is.EqualTo(expectedResult));
            connection.Close();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ColocConnection_Properties(bool isServer)
        {
            ColocNetworkConnection connection = CreateConnection(isServer);

            (INetworkStream? _, IMultiplexedNetworkStreamFactory? _, NetworkConnectionInformation information) =
                await connection.ConnectAsync(default);

            Assert.That(information.LocalEndpoint, Is.EqualTo(Endpoint.FromString("ice+coloc://host")));
            Assert.That(information.RemoteEndpoint, Is.EqualTo(Endpoint.FromString("ice+coloc://host")));
            Assert.That(information.IdleTimeout, Is.EqualTo(TimeSpan.MaxValue));
            Assert.That(connection.IsDatagram, Is.False);

            connection.Close();
        }

        [Test]
        public async Task ColocConnection_LastActivity()
        {
            ColocNetworkConnection connection = CreateConnection(false);

            (INetworkStream? stream, IMultiplexedNetworkStreamFactory? _, NetworkConnectionInformation _) =
                await connection.ConnectAsync(default);

            // Coloc connections are not closed by ACM.
            // TODO: should they?

            await Task.Delay(2);
            await stream!.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[1] }, default);
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
