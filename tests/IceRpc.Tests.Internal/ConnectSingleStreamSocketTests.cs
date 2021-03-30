// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Internal
{
    // Testing the Ice1 and Ice2 protocol here is useful because of the handling of secure vs non-secure
    // incoming connection which is different (with Ice2, the acceptor peeks a byte on the socket to
    // figure out if the outgoing connection is a secure or non-secure connection).
    [TestFixture(Protocol.Ice1, "tcp", NonSecure.Always, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice1, "ssl", NonSecure.Never, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice2, "tcp", NonSecure.Always, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice2, "tcp", NonSecure.Never, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice2, "ws", NonSecure.Always, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice2, "ws", NonSecure.Never, AddressFamily.InterNetwork)]
    [TestFixture(Protocol.Ice1, "tcp", NonSecure.Always, AddressFamily.InterNetworkV6)]
    [TestFixture(Protocol.Ice2, "tcp", NonSecure.Never, AddressFamily.InterNetworkV6)]
    [Timeout(5000)]
    public class ConnectSingleStreamSocketTests : SocketBaseTest
    {
        public ConnectSingleStreamSocketTests(
            Protocol protocol,
            string transport,
            NonSecure nonSecure,
            AddressFamily addressFamily)
            : base(protocol, transport, nonSecure, addressFamily)
        {
        }

        [Test]
        public void ConnectSingleStreamSocket_ConnectAsync_ConnectionRefusedException()
        {
            using SingleStreamSocket clientSocket = CreateClientSocket();
            Assert.ThrowsAsync<ConnectionRefusedException>(
                async () => await clientSocket.ConnectAsync(
                    ClientEndpoint,
                    ClientAuthenticationOptions,
                    default));
        }

        [Test]
        public void ConnectSingleStreamSocket_ConnectAsync_OperationCanceledException()
        {
            using IAcceptor acceptor = CreateAcceptor();

            using var source = new CancellationTokenSource();
            if (!IsSecure && TransportName == "tcp")
            {
                // ConnectAsync might complete synchronously with TCP
            }
            else
            {
                using SingleStreamSocket clientSocket = CreateClientSocket();
                ValueTask<SingleStreamSocket> connectTask =
                    clientSocket.ConnectAsync(
                        ClientEndpoint,
                        ClientAuthenticationOptions,
                        source.Token);
                source.Cancel();
                Assert.CatchAsync<OperationCanceledException>(async () => await connectTask);
            }

            using var source2 = new CancellationTokenSource();
            source2.Cancel();
            using SingleStreamSocket clientSocket2 = CreateClientSocket();
            Assert.CatchAsync<OperationCanceledException>(
                async () => await clientSocket2.ConnectAsync(
                    ClientEndpoint,
                    ClientAuthenticationOptions,
                    source2.Token));
        }

        private SingleStreamSocket CreateClientSocket()
        {
            var endpoint = (TcpEndpoint)ClientEndpoint;
            EndPoint addr = new IPEndPoint(endpoint.Address, endpoint.Port);
            TcpOptions tcpOptions = ClientConnectionOptions.TransportOptions as TcpOptions ?? TcpOptions.Default;
            SingleStreamSocket socket = endpoint.CreateSocket(addr, tcpOptions, Logger);
            MultiStreamOverSingleStreamSocket multiStreamSocket = ClientEndpoint.Protocol switch
            {
                Protocol.Ice1 => new Ice1NetworkSocket(ClientEndpoint, socket, ClientConnectionOptions),
                _ => new SlicSocket(ClientEndpoint, socket, ClientConnectionOptions)
            };
            Connection connection = endpoint.CreateConnection(multiStreamSocket, ClientConnectionOptions, server: null);
            return (connection.Socket as MultiStreamOverSingleStreamSocket)!.Underlying;
        }
    }
}
