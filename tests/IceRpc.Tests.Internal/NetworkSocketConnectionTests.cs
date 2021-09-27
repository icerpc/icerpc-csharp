// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using NUnit.Framework;

#pragma warning disable CA2000 // NetworkSocketStub is Disposed by the NetworkSocketConnection

namespace IceRpc.Tests.Internal
{
    public class NetworkSocketConnectionTests
    {
        [Test]
        public async Task NetworkSocketConnection_ConnectAsync()
        {
            using var connection = new NetworkSocketConnection(
                new NetworkSocketStub(isDatagram: false),
                Endpoint.FromString("ice+tcp://host"),
                isServer: false,
                TimeSpan.FromSeconds(10),
                slicOptions: new(),
                null!);

            await connection.ConnectAsync(default);

            var stub = (NetworkSocketStub)connection.NetworkSocket;
            Assert.That(stub.Endpoint, Is.EqualTo(Endpoint.FromString("ice+tcp://host")));
        }

        [Test]
        public void NetworkSocketConnection_Dispose()
        {
            var connection = new NetworkSocketConnection(
                new NetworkSocketStub(isDatagram: false),
                Endpoint.FromString("ice+tcp://host"),
                isServer: false,
                TimeSpan.FromSeconds(10),
                slicOptions: new(),
                null!);
            connection.Dispose();
            Assert.That(((NetworkSocketStub)connection.NetworkSocket).Disposed, Is.True);
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
            using var connection = new NetworkSocketConnection(
                new NetworkSocketStub(isDatagram: false),
                endpoint,
                isServer: isServer,
                TimeSpan.FromSeconds(10),
                slicOptions: new(),
                null!);

            await connection.ConnectAsync(default);

            var stub = (NetworkSocketStub)connection.NetworkSocket;
            Assert.That(connection.HasCompatibleParams(otherEndpoint), Is.EqualTo(expectedResult));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void NetworkSocketConnection_Properties(bool isServer, bool isDatagram)
        {
            using var connection = new NetworkSocketConnection(
                new NetworkSocketStub(isDatagram),
                Endpoint.FromString("ice+tcp://host"),
                isServer: isServer,
                TimeSpan.FromSeconds(10),
                slicOptions: new(),
                null!);

            Assert.That(connection.LocalEndpoint, Is.EqualTo(isServer ? Endpoint.FromString("ice+tcp://host") : null));
            Assert.That(connection.RemoteEndpoint, Is.EqualTo(isServer ? null : Endpoint.FromString("ice+tcp://host")));
            Assert.That(connection.IsServer, Is.EqualTo(isServer));
            Assert.That(connection.IdleTimeout, Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(connection.IsDatagram, Is.EqualTo(isDatagram));
        }

        [Test]
        public async Task NetworkSocketConnection_LastActivity()
        {
            using var connection = new NetworkSocketConnection(
                new NetworkSocketStub(false),
                Endpoint.FromString("ice+tcp://host"),
                isServer: false,
                TimeSpan.FromSeconds(10),
                slicOptions: new(),
                null!);

            ISingleStreamConnection stream = await connection.GetSingleStreamConnectionAsync(default);

            TimeSpan lastActivity = connection.LastActivity;
            await Task.Delay(2);
            await stream.SendAsync(new ReadOnlyMemory<byte>[] { new byte[1] }, default);
            Assert.That(connection.LastActivity, Is.GreaterThan(lastActivity));

            lastActivity = connection.LastActivity;
            await Task.Delay(2);
            await stream.SendAsync(new byte[1], default);
            Assert.That(connection.LastActivity, Is.GreaterThan(lastActivity));

            lastActivity = connection.LastActivity;
            await Task.Delay(2);
            await stream.ReceiveAsync(new byte[1], default);
            Assert.That(connection.LastActivity, Is.GreaterThan(lastActivity));
        }
    }
}
