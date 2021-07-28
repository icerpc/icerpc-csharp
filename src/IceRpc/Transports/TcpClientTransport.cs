// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IClientTransport"/> for the tcp and ssl transports.</summary>
    public class TcpClientTransport : IClientTransport
    {
        MultiStreamConnection IClientTransport.CreateConnection(
             Endpoint remoteEndpoint,
             ClientConnectionOptions options,
             ILogger logger)
        {
            // First verify all parameters:
            bool? tls = remoteEndpoint.ParseTcpParams().Tls;

            if (remoteEndpoint.Protocol == Protocol.Ice1)
            {
                tls = remoteEndpoint.Transport == TransportNames.Ssl;
            }
            else if (tls == null)
            {
                // TODO: add ability to override this default tls=true through some options
                tls = true;
                remoteEndpoint = remoteEndpoint with
                {
                    Params = remoteEndpoint.Params.Add(new EndpointParam("tls", "true"))
                };
            }

            TcpOptions tcpOptions = options.TransportOptions as TcpOptions ?? TcpOptions.Default;

            EndPoint netEndPoint = IPAddress.TryParse(remoteEndpoint.Host, out IPAddress? ipAddress) ?
                new IPEndPoint(ipAddress, remoteEndpoint.Port) :
                new DnsEndPoint(remoteEndpoint.Host, remoteEndpoint.Port);

            // We still specify the address family for the socket if an address is set to ensure an IPv4 socket is
            // created if the address is an IPv4 address.
            Socket socket = ipAddress == null ?
                new Socket(SocketType.Stream, ProtocolType.Tcp) :
                new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            try
            {
                if (ipAddress?.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    socket.DualMode = !tcpOptions.IsIPv6Only;
                }

                if (tcpOptions.LocalEndPoint is IPEndPoint localEndPoint)
                {
                    socket.Bind(localEndPoint);
                }

                socket.SetBufferSize(tcpOptions.ReceiveBufferSize,
                                     tcpOptions.SendBufferSize,
                                     remoteEndpoint.Transport,
                                     logger);
                socket.NoDelay = true;
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                throw new TransportException(ex);
            }

            var tcpSocket = new TcpSocket(socket, logger, tls, netEndPoint);
            return NetworkSocketConnection.FromNetworkSocket(tcpSocket, remoteEndpoint, options);
        }
    }
}
