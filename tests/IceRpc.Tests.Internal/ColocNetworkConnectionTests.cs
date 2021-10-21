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
            ISimpleNetworkConnection connection = CreateConnection(false);
            connection.Close();
            connection.Close();
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        public void ColocNetworkConnection_HasCompatibleParams(bool isServer, bool expectedResult)
        {
            ISimpleNetworkConnection connection = CreateConnection(isServer);
            Assert.That(connection.HasCompatibleParams(Endpoint.FromString("ice+coloc://host")),
                        Is.EqualTo(expectedResult));
            connection.Close();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ColocNetworkConnection_Properties(bool isServer)
        {
            ISimpleNetworkConnection connection = CreateConnection(isServer);

            (ISimpleStream? _, NetworkConnectionInformation information) = await connection.ConnectAsync(default);

            Assert.That(information.LocalEndpoint, Is.EqualTo(Endpoint.FromString("ice+coloc://host")));
            Assert.That(information.RemoteEndpoint, Is.EqualTo(Endpoint.FromString("ice+coloc://host")));
            Assert.That(information.IdleTimeout, Is.EqualTo(TimeSpan.MaxValue));

            connection.Close();
        }

        [Test]
        public async Task ColocNetworkConnection_LastActivity()
        {
            ISimpleNetworkConnection connection = CreateConnection(false);

            (ISimpleStream stream, NetworkConnectionInformation _) = await connection.ConnectAsync(default);

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

        private static ISimpleNetworkConnection CreateConnection(bool isServer)
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
