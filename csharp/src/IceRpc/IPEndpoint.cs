// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>The base class for IP-based endpoints: TcpEndpoint, UdpEndpoint.</summary>
    internal abstract class IPEndpoint : Endpoint
    {
        public override string? this[string option] =>
            option switch
            {
                "source-address" => SourceAddress?.ToString(),
                "ipv6-only" => IsIPv6Only ? "true" : "false",
                _ => base[option],
            };

        protected internal override bool HasOptions => Protocol == Protocol.Ice1 || SourceAddress != null;

        // The default port with ice1 is 0.
        protected internal override ushort DefaultPort => Protocol == Protocol.Ice1 ? (ushort)0 : DefaultIPPort;

        protected internal override bool HasDnsHost => Address == IPAddress.None;

        internal const ushort DefaultIPPort = 4062;

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
                        // Assume it's a DNS name
                        _address = IPAddress.None;
                    }
                }
                return _address;
            }
        }

        /// <summary>Whether IPv6 sockets created from this endpoint are dual-mode or IPv6 only.</summary>
        internal bool IsIPv6Only { get; }

        /// <summary>The source address of this IP endpoint.</summary>
        internal IPAddress? SourceAddress { get; }

        private IPAddress? _address;

        public override bool Equals(Endpoint? other) =>
            other is IPEndpoint ipEndpoint &&
                Equals(SourceAddress, ipEndpoint.SourceAddress) &&
                IsIPv6Only == ipEndpoint.IsIPv6Only &&
                base.Equals(other);

        public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), SourceAddress, IsIPv6Only);

        public override bool IsLocal(Endpoint endpoint)
        {
            // TODO: revisit, is connection ID gone?

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
                if (!Equals(SourceAddress, ipEndpoint.SourceAddress))
                {
                    return false;
                }
                if (IsIPv6Only != ipEndpoint.IsIPv6Only)
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

        protected internal override async Task<Connection> ConnectAsync(
            NonSecure preferNonSecure,
            object? label,
            CancellationToken cancel)
        {
            bool secureOnly = preferNonSecure switch
            {
                NonSecure.SameHost => true,    // TODO check if Host is the same host
                NonSecure.TrustedHost => true, // TODO check if Host is a trusted host
                NonSecure.Always => false,
                _ => true
            };
            Connection connection = CreateConnection(label, cancel);
            await connection.Socket.ConnectAsync(secureOnly, cancel).ConfigureAwait(false);
            Debug.Assert(connection.CanTrust(preferNonSecure));
            return connection;
        }

        protected internal abstract Connection CreateConnection(object? label, CancellationToken cancel);

        protected internal override void WriteOptions(OutputStream ostr)
        {
            Debug.Assert(Protocol == Protocol.Ice1);
            ostr.WriteString(Host);
            ostr.WriteInt(Port);
        }

        protected internal override void AppendOptions(StringBuilder sb, char optionSeparator)
        {
            if (Protocol == Protocol.Ice1)
            {
                Debug.Assert(Host.Length > 0);

                {
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
                }

                sb.Append(" -p ");
                sb.Append(Port.ToString(CultureInfo.InvariantCulture));

                if (IsIPv6Only)
                {
                    sb.Append(" --ipv6Only");
                }

                if (SourceAddress != null)
                {
                    string sourceAddr = SourceAddress.ToString();
                    bool addQuote = sourceAddr.IndexOf(':') != -1;
                    sb.Append(" --sourceAddress ");
                    if (addQuote)
                    {
                        sb.Append('"');
                    }
                    sb.Append(sourceAddr);
                    if (addQuote)
                    {
                        sb.Append('"');
                    }
                }
            }
            else
            {
                if (IsIPv6Only)
                {
                    sb.Append("ipv6-only=true");
                }

                if (SourceAddress != null)
                {
                    sb.Append("source-address=");
                    sb.Append(SourceAddress);
                }
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
                clone._address = _address;
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
            bool serverEndpoint,
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
                    host = serverEndpoint ? "::0" :
                        throw new FormatException($"`-h *' not valid for proxy endpoint `{endpointString}'");
                }

                if (serverEndpoint)
                {
                    if (!IPAddress.TryParse(host, out IPAddress? _))
                    {
                        throw new FormatException($"invalid IP address `{host}' in server endpoint `{endpointString}'");
                    }
                }
                else if (IPAddress.TryParse(host, out IPAddress? address) &&
                        (address!.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)))
                {
                    throw new FormatException("0.0.0.0 or [::0] is not a valid host in a proxy endpoint");
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

        // Constructor for ice1/ice2 unmarshaling.
        private protected IPEndpoint(EndpointData data, Communicator communicator, Protocol protocol)
            : base(data, communicator, protocol)
        {
            if (data.Host.Length == 0)
            {
                throw new InvalidDataException("endpoint host is empty");
            }

            SourceAddress = communicator.DefaultSourceAddress;
        }

        // Constructor for ice1 endpoint parsing.
        private protected IPEndpoint(
            EndpointData data,
            Dictionary<string, string?> options,
            Communicator communicator,
            bool serverEndpoint,
            string endpointString)
            : base(data, communicator, Protocol.Ice1)
        {
            if (options.TryGetValue("--sourceAddress", out string? argument))
            {
                if (serverEndpoint)
                {
                    throw new FormatException(
                        $"`--sourceAddress' not valid for a server endpoint `{endpointString}'");
                }
                if (argument == null)
                {
                    throw new FormatException(
                        $"no argument provided for --sourceAddress option in endpoint `{endpointString}'");
                }
                try
                {
                    SourceAddress = IPAddress.Parse(argument);
                }
                catch (Exception ex)
                {
                    throw new FormatException(
                        $"invalid IP address provided for --sourceAddress option in endpoint `{endpointString}'", ex);
                }
                options.Remove("--sourceAddress");
            }
            else if (!serverEndpoint)
            {
                SourceAddress = Communicator.DefaultSourceAddress;
            }
            // else SourceAddress remains null

            if (options.TryGetValue("--ipv6Only", out argument))
            {
                if (!serverEndpoint)
                {
                    throw new FormatException(
                        $"`--ipv6Only' not valid for a server endpoint `{endpointString}'");
                }
                if (argument != null)
                {
                    throw new FormatException($"--ipv6Only does not accept an argument in endpoint `{endpointString}'");
                }
                IsIPv6Only = true;
                options.Remove("--ipv6Only");
            }
            // else IsIPv6Only remains false (default initialized)
        }

        // Constructor for ice2 parsing.
        private protected IPEndpoint(
            EndpointData data,
            Dictionary<string, string> options,
            Communicator communicator,
            bool serverEndpoint)
            : base(data, communicator, Protocol.Ice2)
        {
            if (!serverEndpoint && IPAddress.TryParse(data.Host, out IPAddress? address) &&
                (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)))
            {
                throw new ArgumentException("0.0.0.0 or [::0] is not a valid host in a proxy endpoint", nameof(data));
            }

            if (serverEndpoint)
            {
                if (options.TryGetValue("ipv6-only", out string? value))
                {
                    IsIPv6Only = bool.Parse(value);
                    options.Remove("ipv6-only");
                }
            }
            else // parsing a URI that represents a proxy
            {
                if (options.TryGetValue("source-address", out string? value))
                {
                    // IPAddress.Parse apparently accepts IPv6 addresses in square brackets
                    SourceAddress = IPAddress.Parse(value);
                    options.Remove("source-address");
                }
                else
                {
                    SourceAddress = Communicator.DefaultSourceAddress;
                }
            }
        }

        // Constructor for Clone
        private protected IPEndpoint(IPEndpoint endpoint, string host, ushort port)
            : base(new EndpointData(endpoint.Transport, host, port, endpoint.Data.Options),
                   endpoint.Communicator,
                   endpoint.Protocol)
        {
            SourceAddress = endpoint.SourceAddress;
            IsIPv6Only = endpoint.IsIPv6Only;
        }

        /// <summary>Creates a clone with the specified host and port.</summary>
        private protected abstract IPEndpoint Clone(string host, ushort port);
    }
}
