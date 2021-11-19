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
        public async Task ColocNetworkConnection_Close()
        {
            ISimpleNetworkConnection connection = CreateConnection(false);
            await connection.DisposeAsync().ConfigureAwait(false);
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        public async Task ColocNetworkConnection_HasCompatibleParams(bool isServer, bool expectedResult)
        {
            await using ISimpleNetworkConnection connection = CreateConnection(isServer);
            Assert.That(connection.HasCompatibleParams(Endpoint.FromString("ice+coloc://host")),
                        Is.EqualTo(expectedResult));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ColocNetworkConnection_Properties(bool isServer)
        {
            await using ISimpleNetworkConnection connection = CreateConnection(isServer);

            NetworkConnectionInformation information = await connection.ConnectAsync(default);

            Assert.That(information.LocalEndpoint, Is.EqualTo(Endpoint.FromString("ice+coloc://host")));
            Assert.That(information.RemoteEndpoint, Is.EqualTo(Endpoint.FromString("ice+coloc://host")));
            Assert.That(information.IdleTimeout, Is.EqualTo(TimeSpan.MaxValue));
        }

        [Test]
        public async Task ColocNetworkConnection_LastActivity()
        {
            await using ISimpleNetworkConnection connection = CreateConnection(false);

            NetworkConnectionInformation _ = await connection.ConnectAsync(default);

            // Coloc connections are not closed by ACM.
            // TODO: should they?

            await Task.Delay(2);
            await connection.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[1] }, default);
            Assert.That(connection.LastActivity, Is.EqualTo(TimeSpan.Zero));

            await Task.Delay(2);
            await connection.ReadAsync(new byte[1], default);
            Assert.That(connection.LastActivity, Is.EqualTo(TimeSpan.Zero));
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
