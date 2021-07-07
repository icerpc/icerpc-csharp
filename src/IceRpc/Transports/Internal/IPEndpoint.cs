// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace IceRpc.Transports.Internal
{
    /// <summary>The base class for IP-based endpoints: TcpEndpoint, UdpEndpoint.</summary>
    internal abstract class IPEndpoint : Endpoint
    {
        public override ushort DefaultPort => Protocol == Protocol.Ice1 ? (ushort)0 : DefaultUriPort;

        internal const ushort DefaultUriPort = 4062;

        /// <summary>When Host is an IP address, returns the parsed IP address. Otherwise, when Host is a DNS name,
        /// returns IPAddress.None.</summary>
        internal IPAddress Address
        {
            get
            {
                if (_address == null)
                {
                    if (!IPAddress.TryParse(Host, out _address))
                    {
                        _address = IPAddress.None; // assume it's a DNS name
                    }
                }
                return _address;
            }
        }

        /// <summary>Returns true when Host is a DNS name.</summary>
        private protected bool HasDnsHost => Address == IPAddress.None;

        private IPAddress? _address;

        protected internal override void WriteOptions11(IceEncoder iceEncoder)
        {
            Debug.Assert(Protocol == Protocol.Ice1 && iceEncoder.Encoding == Encoding.V11);
            iceEncoder.WriteString(Host);
            iceEncoder.WriteInt(Port);
        }

        private protected static void SetBufferSize(
            Socket socket,
            int? receiveSize,
            int? sendSize,
            Transport transport,
            ILogger logger)
        {
            if (receiveSize != null)
            {
                // Try to set the buffer size. The kernel will silently adjust the size to an acceptable value. Then
                // read the size back to get the size that was actually set.
                socket.ReceiveBufferSize = receiveSize.Value;
                if (socket.ReceiveBufferSize != receiveSize)
                {
                    logger.LogReceiveBufferSizeAdjusted(transport, receiveSize.Value, socket.ReceiveBufferSize);
                }
            }

            if (sendSize != null)
            {
                // Try to set the buffer size. The kernel will silently adjust the size to an acceptable value. Then
                // read the size back to get the size that was actually set.
                socket.SendBufferSize = sendSize.Value;
                if (socket.SendBufferSize != sendSize)
                {
                    logger.LogSendBufferSizeAdjusted(transport, sendSize.Value, socket.SendBufferSize);
                }
            }
        }

        // Main constructor
        private protected IPEndpoint(EndpointData data, Protocol protocol)
            : base(data, protocol)
        {
            if (data.Host.Length == 0)
            {
                throw new InvalidDataException("endpoint host is empty");
            }
        }

        // Constructor for Clone
        private protected IPEndpoint(Endpoint endpoint, string host, ushort port)
            : this(new EndpointData(endpoint.Transport, host, port, endpoint.Data.Options), endpoint.Protocol)
        {
        }
    }
}
