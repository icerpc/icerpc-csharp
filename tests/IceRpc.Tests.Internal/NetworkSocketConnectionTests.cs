// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

#pragma warning disable CA2000 // NetworkSocketStub is Disposed by the NetworkSocketConnection

namespace IceRpc.Tests.Internal
{
    public class NetworkSocketConnectionTests
    {
        [Test]
        public void NetworkSocketConnection_Dispose()
        {
            var connection = new SocketNetworkConnection(
                new NetworkSocketStub(isDatagram: false),
                Endpoint.FromString("ice+tcp://host"),
                isServer: false,
                TimeSpan.FromSeconds(10));
            connection.Close();
            connection.Close();
            Assert.That(((NetworkSocketStub)connection.NetworkSocket).Disposed, Is.True);
        }

        [Test]
        public async Task NetworkSocketConnection_GetSingleStreamConnectionAsync()
        {
            var connection = new SocketNetworkConnection(
                new NetworkSocketStub(isDatagram: false),
                Endpoint.FromString("ice+tcp://host"),
                isServer: false,
                TimeSpan.FromSeconds(10));

            await connection.ConnectAsync(default);

            var stub = (NetworkSocketStub)connection.NetworkSocket;
            Assert.That(stub.Connected, Is.True);
            Assert.That(stub.Endpoint, Is.EqualTo(Endpoint.FromString("ice+tcp://host")));

            connection.Close();
        }

        [TestCase(true, "ice+tcp://host", "ice+tcp://host", false)]
        [TestCase(false, "ice+tcp://host", "ice+tcp://host", true)]
        [TestCase(false, "ice+tcp://host", "ice+tcp://host1", false)]
        [TestCase(false, "ice+tcp://host?tls=false", "ice+tcp://host?tls=false", true)]
        [TestCase(false, "ice+tcp://host?tls=false", "ice+tcp://host?tls=true", false)]
        public async Task NetworkSocketConnection_HasCompatibleParams(
            bool isServer,
            string endpoint,
            string otherEndpoint,
            bool expectedResult)
        {
            var connection = new SocketNetworkConnection(
                new NetworkSocketStub(isDatagram: false),
                endpoint,
                isServer: isServer,
                TimeSpan.FromSeconds(10));

            await connection.ConnectAsync(default);

            Assert.That(connection.HasCompatibleParams(otherEndpoint), Is.EqualTo(expectedResult));
            connection.Close();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NetworkSocketConnection_Properties(bool isDatagram)
        {
            var connection = new SocketNetworkConnection(
                new NetworkSocketStub(isDatagram),
                Endpoint.FromString("ice+tcp://host"),
                isServer: false,
                TimeSpan.FromSeconds(10));

            Assert.That(connection.IsDatagram, Is.EqualTo(isDatagram));
            connection.Close();
        }

        [Test]
        public async Task NetworkSocketConnection_LastActivity()
        {
            var connection = new SocketNetworkConnection(
                new NetworkSocketStub(false),
                Endpoint.FromString("ice+tcp://host"),
                isServer: false,
                TimeSpan.FromSeconds(10));

            await connection.ConnectAsync(default);

            INetworkStream stream = connection.GetNetworkStream();

            TimeSpan lastActivity = connection.LastActivity;
            await Task.Delay(2);
            await stream.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[1] }, default);
            Assert.That(connection.LastActivity, Is.GreaterThan(lastActivity));

            lastActivity = connection.LastActivity;
            await Task.Delay(2);
            await stream.ReadAsync(new byte[1], default);
            Assert.That(connection.LastActivity, Is.GreaterThan(lastActivity));

            connection.Close();
        }
    }
}
