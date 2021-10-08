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
            var connection = new NetworkSocketConnection(
                new NetworkSocketStub(isDatagram: false),
                Endpoint.FromString("ice+tcp://host"),
                isServer: false,
                TimeSpan.FromSeconds(10),
                slicOptions: new(),
                null!);
            connection.Close();
            connection.Close();
            Assert.That(((NetworkSocketStub)connection.NetworkSocket).Disposed, Is.True);
        }

        [Test]
        public async Task NetworkSocketConnection_GetSingleStreamConnectionAsync()
        {
            var connection = new NetworkSocketConnection(
                new NetworkSocketStub(isDatagram: false),
                Endpoint.FromString("ice+tcp://host"),
                isServer: false,
                TimeSpan.FromSeconds(10),
                slicOptions: new(),
                NullLogger.Instance);

            _ = await connection.GetSingleStreamConnectionAsync(default);

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
            var connection = new NetworkSocketConnection(
                new NetworkSocketStub(isDatagram: false),
                endpoint,
                isServer: isServer,
                TimeSpan.FromSeconds(10),
                slicOptions: new(),
                null!);

            _ = await connection.GetSingleStreamConnectionAsync(default);

            Assert.That(connection.HasCompatibleParams(otherEndpoint), Is.EqualTo(expectedResult));
            connection.Close();
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void NetworkSocketConnection_Properties(bool isServer, bool isDatagram)
        {
            var connection = new NetworkSocketConnection(
                new NetworkSocketStub(isDatagram),
                Endpoint.FromString("ice+tcp://host"),
                isServer: isServer,
                TimeSpan.FromSeconds(10),
                slicOptions: new(),
                null!);

            Assert.That(connection.LocalEndpoint, Is.EqualTo(isServer ? Endpoint.FromString("ice+tcp://host") : null));
            Assert.That(connection.RemoteEndpoint, Is.EqualTo(isServer ? null : Endpoint.FromString("ice+tcp://host")));
            Assert.That(connection.IdleTimeout, Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(connection.IsDatagram, Is.EqualTo(isDatagram));
            connection.Close();
        }

        [Test]
        public async Task NetworkSocketConnection_LastActivity()
        {
            var connection = new NetworkSocketConnection(
                new NetworkSocketStub(false),
                Endpoint.FromString("ice+tcp://host"),
                isServer: false,
                TimeSpan.FromSeconds(10),
                slicOptions: new(),
                NullLogger.Instance);

            ISingleStreamConnection stream = await connection.GetSingleStreamConnectionAsync(default);

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
