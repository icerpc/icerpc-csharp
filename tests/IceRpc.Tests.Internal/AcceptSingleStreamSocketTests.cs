// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Internal
{
    // Testing the Ice1 and Ice2 protocol here is useful because of the handling of secure vs non-secure
    // incoming connection which is different (with Ice2, the acceptor peeks a byte on the socket to
    // figure out if the outgoing connection is a secure or non-secure connection).
    [TestFixture(Protocol.Ice2, "tcp", false, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice2, "tcp", true, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice2, "ws", false, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice2, "ws", true, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice1, "tcp", false, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice1, "ssl", true, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice1, "tcp", false, AddressFamily.InterNetworkV6)]
    [TestFixture(Protocol.Ice2, "tcp", false, AddressFamily.InterNetworkV6)]
    [Timeout(5000)]
    public class AcceptSingleStreamSocketTests : SocketBaseTest
    {
        public AcceptSingleStreamSocketTests(
            Protocol protocol,
            string transport,
            bool tls,
            AddressFamily addressFamily)
            : base(protocol, transport, tls, addressFamily)
        {
        }

        [Test]
        public async Task AcceptSingleStreamSocket_Acceptor_AcceptAsync()
        {
            using IAcceptor acceptor = CreateAcceptor();
            ValueTask<SingleStreamConnection> acceptTask = CreateServerSocketAsync(acceptor);

            using SingleStreamConnection clientSocket = CreateClientSocket();
            ValueTask<(SingleStreamConnection, Endpoint)> connectTask = clientSocket.ConnectAsync(
                ClientEndpoint,
                ClientAuthenticationOptions,
                default);
            using SingleStreamConnection serverSocket = await acceptTask;
        }

        [Test]
        public void AcceptSingleStreamSocket_Acceptor_Constructor_TransportException()
        {
            using IAcceptor acceptor = CreateAcceptor();
            Assert.Throws<TransportException>(() => CreateAcceptor());
        }

        [Test]
        public async Task AcceptSingleStreamSocket_AcceptAsync()
        {
            using IAcceptor acceptor = CreateAcceptor();
            ValueTask<SingleStreamConnection> acceptTask = CreateServerSocketAsync(acceptor);

            using SingleStreamConnection clientSocket = CreateClientSocket();
            ValueTask<(SingleStreamConnection, Endpoint)> connectTask = clientSocket.ConnectAsync(
                ClientEndpoint,
                ClientAuthenticationOptions,
                default);

            using SingleStreamConnection serverSocket = await acceptTask;

            ValueTask<(SingleStreamConnection, Endpoint?)> acceptTask2 = serverSocket.AcceptAsync(
                ServerEndpoint,
                ServerAuthenticationOptions,
                default);

            await connectTask;

            if (ClientEndpoint.Protocol == Protocol.Ice2 && TransportName == "tcp")
            {
                await clientSocket.SendAsync(new List<ArraySegment<byte>> { new byte[1] }, default);
            }

            (SingleStreamConnection socket, Endpoint _) = await acceptTask2;

            // The SslConnection is returned if a secure connection is requested.
            if (IsSecure && TransportName != "ws")
            {
                Assert.IsInstanceOf<SslConnection>(socket);
            }
            else
            {
                Assert.IsNotInstanceOf<SslConnection>(socket);
            }
        }

        // We eventually retry this test if it fails. The AcceptAsync can indeed not always fail if for
        // example the server SSL handshake completes before the RST is received.
        [Test]
        public async Task AcceptSingleStreamSocket_AcceptAsync_ConnectionLostExceptionAsync()
        {
            using IAcceptor acceptor = CreateAcceptor();
            ValueTask<SingleStreamConnection> acceptTask = CreateServerSocketAsync(acceptor);

            SingleStreamConnection clientSocket = CreateClientSocket();

            // We don't use clientSocket.ConnectAsync() here as this would start the TLS handshake for secure
            // connections and AcceptAsync would sometime succeed.
            await clientSocket.NetworkSocket!.ConnectAsync(
                new DnsEndPoint(ClientEndpoint.Host, ClientEndpoint.Port)).ConfigureAwait(false);

            using SingleStreamConnection serverSocket = await acceptTask;

            clientSocket.Dispose();

            AsyncTestDelegate testDelegate;
            if (!IsSecure && ClientEndpoint.Protocol == Protocol.Ice1 && TransportName == "tcp")
            {
                // AcceptAsync is a no-op for Ice1 non-secure TCP connections so it won't throw.
                await serverSocket.AcceptAsync(
                    ServerEndpoint,
                    ServerAuthenticationOptions,
                    default);
                testDelegate = async () => await serverSocket.ReceiveAsync(new byte[1], default);
            }
            else
            {
                testDelegate = async () => await serverSocket.AcceptAsync(
                    ServerEndpoint,
                    ServerAuthenticationOptions,
                    default);
            }
            Assert.ThrowsAsync<ConnectionLostException>(testDelegate);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void AcceptSingleStreamSocket_Acceptor_AddressReuse(bool wildcard1, bool wildcard2)
        {
            IAcceptor acceptor;
            if (wildcard1)
            {
                var serverData = new EndpointData(
                    ServerEndpoint.Transport,
                    "::0",
                    ServerEndpoint.Port,
                    ServerEndpoint.Data.Options);
                var serverEndpoint = TcpEndpoint.CreateEndpoint(serverData, ServerEndpoint.Protocol);
                acceptor = serverEndpoint.CreateAcceptor(ServerConnectionOptions, Logger);
            }
            else
            {
                acceptor = CreateAcceptor();
            }

            if (wildcard2)
            {
                var serverData = new EndpointData(
                    ServerEndpoint.Transport,
                    "::0",
                    ServerEndpoint.Port,
                    ServerEndpoint.Data.Options);
                var serverEndpoint = TcpEndpoint.CreateEndpoint(serverData, ServerEndpoint.Protocol);

                if (OperatingSystem.IsMacOS())
                {
                    // On macOS, it's still possible to bind to a specific address even if a socket is bound
                    // to the wildcard address.
                    Assert.DoesNotThrow(
                        () => serverEndpoint.CreateAcceptor(ServerConnectionOptions, Logger).Dispose());
                }
                else
                {
                    Assert.Catch<TransportException>(
                        () => serverEndpoint.CreateAcceptor(ServerConnectionOptions, Logger));
                }
            }
            else
            {
                if (wildcard1 && OperatingSystem.IsMacOS())
                {
                    // On macOS, it's still possible to bind to a specific address even if a socket is bound
                    // to the wildcard address.
                    Assert.DoesNotThrow(() => CreateAcceptor().Dispose());
                }
                else
                {
                    Assert.Catch<TransportException>(() => CreateAcceptor());
                }
            }

            acceptor.Dispose();
        }

        [Test]
        public async Task AcceptSingleStreamSocket_AcceptAsync_OperationCanceledExceptionAsync()
        {
            using IAcceptor acceptor = CreateAcceptor();

            using SingleStreamConnection clientSocket = CreateClientSocket();
            ValueTask<(SingleStreamConnection, Endpoint)> connectTask = clientSocket.ConnectAsync(
                ClientEndpoint,
                ClientAuthenticationOptions,
                default);

            using SingleStreamConnection serverSocket = await CreateServerSocketAsync(acceptor);

            using var source = new CancellationTokenSource();
            source.Cancel();
            ValueTask<(SingleStreamConnection, Endpoint?)> acceptTask = serverSocket.AcceptAsync(
                    ServerEndpoint,
                    ServerAuthenticationOptions,
                    source.Token);

            if (!IsSecure && ClientEndpoint.Protocol == Protocol.Ice1 && TransportName == "tcp")
            {
                // AcceptAsync is a no-op for Ice1 non-secure TCP connections so it won't throw.
                await acceptTask;
            }
            else
            {
                Assert.CatchAsync<OperationCanceledException>(async () => await acceptTask);
            }
        }

        private SingleStreamConnection CreateClientSocket() =>
            (ClientEndpoint.CreateOutgoingConnection(
                ClientConnectionOptions,
                Logger) as MultiStreamOverSingleStreamConnection)!.Underlying;

        private static async ValueTask<SingleStreamConnection> CreateServerSocketAsync(IAcceptor acceptor) =>
            (await acceptor.AcceptAsync() as MultiStreamOverSingleStreamConnection)!.Underlying;
    }
}
