// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace IceRpc
{
    /// <summary>The base class for IP-based endpoints: TcpEndpoint, UdpEndpoint.</summary>
    internal abstract class IPEndpoint : Endpoint
    {
        /// <inherit-doc/>
        public override bool IsProxyCompatible { get; } = true;

        /// <inherit-doc/>
        public override bool IsServerCompatible { get; } = true;

        protected internal override bool HasOptions => Protocol == Protocol.Ice1;

        // The default port with ice1 is 0.
        protected internal override ushort DefaultPort => Protocol == Protocol.Ice1 ? (ushort)0 : DefaultIPPort;

        protected internal override bool HasDnsHost => Address == IPAddress.None;

        internal const ushort DefaultIPPort = 4062;

        /// <summary>When Host is an IP address, returns the parsed IP address. Otherwise, when Host is a DNS name,
        /// returns IPAddress.None.</summary>
        internal IPAddress Address { get; }

        public override bool Equals(Endpoint? other) => other is IPEndpoint && base.Equals(other);

        public override bool IsLocal(Endpoint endpoint)
        {
            if (endpoint is IPEndpoint ipEndpoint)
            {
                // Same as Equals except we don't consider the connection ID

                if (Transport != ipEndpoint.Transport)
                {
                    return false;
                }
                if (Host != ipEndpoint.Host)
                {
                    return false;
                }
                if (Port != ipEndpoint.Port)
                {
                    return false;
                }
                return true;
            }
            else
            {
                return false;
            }
        }

        protected internal override void WriteOptions11(OutputStream ostr)
        {
            Debug.Assert(Protocol == Protocol.Ice1 && ostr.Encoding == Encoding.V11);
            ostr.WriteString(Host);
            ostr.WriteInt(Port);
        }

        protected internal override void AppendOptions(StringBuilder sb, char optionSeparator)
        {
            if (Protocol == Protocol.Ice1)
            {
                Debug.Assert(Host.Length > 0);
                sb.Append(" -h ");
                bool addQuote = Host.IndexOf(':') != -1;
                if (addQuote)
                {
                    sb.Append('"');
                }
                sb.Append(Host);
                if (addQuote)
                {
                    sb.Append('"');
                }

                sb.Append(" -p ");
                sb.Append(Port.ToString(CultureInfo.InvariantCulture));
            }
        }

        protected internal override Endpoint GetPublishedEndpoint(string publishedHost) =>
            publishedHost == Host ? this : Clone(publishedHost, Port);

        internal IPEndpoint Clone(ushort port)
        {
            if (port == Port)
            {
                return this;
            }
            else
            {
                IPEndpoint clone = Clone(Host, port);
                return clone;
            }
        }

        private protected static bool ParseCompress(Dictionary<string, string?> options, string endpointString)
        {
            bool compress = false;

            if (options.TryGetValue("-z", out string? argument))
            {
                if (argument != null)
                {
                    throw new FormatException(
                        $"unexpected argument `{argument}' provided for -z option in `{endpointString}'");
                }
                compress = true;
                options.Remove("-z");
            }
            return compress;
        }

        // Parse host and port from ice1 endpoint string.
        private protected static (string Host, ushort Port) ParseHostAndPort(
            Dictionary<string, string?> options,
            string endpointString)
        {
            string host;
            ushort port = 0;

            if (options.TryGetValue("-h", out string? argument))
            {
                host = argument ??
                    throw new FormatException($"no argument provided for -h option in endpoint `{endpointString}'");

                if (host == "*")
                {
                    // TODO: Should we check that IPv6 is enabled first and use 0.0.0.0 otherwise, or will
                    // ::0 just bind to the IPv4 addresses in this case?
                    host = "::0";
                }

                options.Remove("-h");
            }
            else
            {
                throw new FormatException($"no -h option in endpoint `{endpointString}'");
            }

            if (options.TryGetValue("-p", out argument))
            {
                if (argument == null)
                {
                    throw new FormatException($"no argument provided for -p option in endpoint `{endpointString}'");
                }

                try
                {
                    port = ushort.Parse(argument, CultureInfo.InvariantCulture);
                }
                catch (FormatException ex)
                {
                    throw new FormatException($"invalid port value `{argument}' in endpoint `{endpointString}'", ex);
                }
                options.Remove("-p");
            }
            // else port remains 0

            return (host, port);
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
        private protected IPEndpoint(EndpointData data, Protocol protocol, bool proxyCompatible)
            : base(data, protocol)
        {
            IsProxyCompatible = proxyCompatible;

            if (data.Host.Length == 0)
            {
                throw new InvalidDataException("endpoint host is empty");
            }

            if (IPAddress.TryParse(data.Host, out IPAddress? address))
            {
                Address = address;
                if (Address.Equals(IPAddress.Any) || Address.Equals(IPAddress.IPv6Any))
                {
                    IsProxyCompatible = false; // cannot use 0.0.0.0 or ::0 for a proxy
                }
            }
            else
            {
                // Assume it's a DNS name
                Address = IPAddress.None;
                IsServerCompatible = false; // DNS names are not server-compatible.
            }
        }

        // Constructor for Clone
        private protected IPEndpoint(IPEndpoint endpoint, string host, ushort port)
            : this(new EndpointData(endpoint.Transport, host, port, endpoint.Data.Options),
                   endpoint.Protocol,
                   endpoint.IsProxyCompatible)
        {
        }

        /// <summary>Creates a clone with the specified host and port.</summary>
        private protected abstract IPEndpoint Clone(string host, ushort port);

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
