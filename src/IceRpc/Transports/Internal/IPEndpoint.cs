// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace IceRpc.Transports.Internal
{
    /// <summary>The base class for IP-based endpoints: TcpEndpoint, UdpEndpoint.</summary>
    internal abstract class IPEndpoint : Endpoint
    {
        public override ushort DefaultPort => Protocol == Protocol.Ice1 ? (ushort)0 : DefaultUriPort;

        protected internal override bool HasDnsHost => Address == IPAddress.None;

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

        private protected const ushort DefaultUriPort = 4062;

        private IPAddress? _address;

        public override bool Equals(Endpoint? other) => other is IPEndpoint && base.Equals(other);

        protected internal override void WriteOptions11(OutputStream ostr)
        {
            Debug.Assert(Protocol == Protocol.Ice1 && ostr.Encoding == Encoding.V11);
            ostr.WriteString(Host);
            ostr.WriteInt(Port);
        }

        // Read port for an ice1 endpoint.
        private protected static ushort ReadPort(InputStream istr)
        {
            ushort port;
            checked
            {
                port = (ushort)istr.ReadInt();
            }
            return port;
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
            : this(new EndpointData(endpoint.Transport, host, port, endpoint.Data.Options),
                   endpoint.Protocol)
        {
        }

        private protected void SetBufferSize(Socket socket, int? receiveSize, int? sendSize, ILogger logger)
        {
            try
            {
                if (receiveSize != null)
                {
                    // Try to set the buffer size. The kernel will silently adjust the size to an acceptable value. Then
                    // read the size back to get the size that was actually set.
                    socket.ReceiveBufferSize = receiveSize.Value;
                    if (socket.ReceiveBufferSize != receiveSize)
                    {
                        logger.LogReceiveBufferSizeAdjusted(Transport, receiveSize.Value, socket.ReceiveBufferSize);
                    }
                }

                if (sendSize != null)
                {
                    // Try to set the buffer size. The kernel will silently adjust the size to an acceptable value. Then
                    // read the size back to get the size that was actually set.
                    socket.SendBufferSize = sendSize.Value;
                    if (socket.SendBufferSize != sendSize)
                    {
                        logger.LogSendBufferSizeAdjusted(Transport, sendSize.Value, socket.SendBufferSize);
                    }
                }
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    }
}
