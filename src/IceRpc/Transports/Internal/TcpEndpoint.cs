// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace IceRpc.Transports.Internal
{
    /// <summary>The Endpoint class for the TCP transport.</summary>
    internal class TcpEndpoint : IPEndpoint
    {
        public override bool? IsSecure => _tls;

        public override string? this[string option] =>
            option switch
            {
                "compress" => _hasCompressionFlag ? "true" : null,
                "timeout" => _timeout != DefaultTimeout ?
                             _timeout.TotalMilliseconds.ToString(CultureInfo.InvariantCulture) : null,
                "tls" => Protocol == Protocol.Ice1 ? null : _tls?.ToString().ToLowerInvariant(),
                _ => base[option],
            };

        protected internal override bool HasOptions => Protocol == Protocol.Ice1 || _tls != null;

        /// <summary>The default timeout for ice1 endpoints.</summary>
        internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

        private readonly bool _hasCompressionFlag;
        private readonly TimeSpan _timeout = DefaultTimeout;

        /// <summary>The TLS option of this endpoint.</summary>
        /// <value><c>true</c> means use TLS, <c>false</c> means do no use TLS, and <c>null</c> means the TLS usage is
        /// to be determined. With ice1, the value is never null.</value>
        private readonly bool? _tls;

        public override bool Equals(Endpoint? other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (Protocol == Protocol.Ice1)
            {
                return other is TcpEndpoint tcpEndpoint &&
                    _hasCompressionFlag == tcpEndpoint._hasCompressionFlag &&
                    _timeout == tcpEndpoint._timeout &&
                    base.Equals(tcpEndpoint);
            }
            else
            {
                return other is TcpEndpoint tcpEndpoint && _tls == tcpEndpoint._tls && base.Equals(other);
            }
        }

        protected internal override void AppendOptions(StringBuilder sb, char optionSeparator)
        {
            base.AppendOptions(sb, optionSeparator);

            if (Protocol == Protocol.Ice1)
            {
                // InfiniteTimeSpan yields -1 and we use -1 instead of "infinite" for compatibility with Ice 3.5.
                sb.Append(" -t ");
                sb.Append(_timeout.TotalMilliseconds);

                if (_hasCompressionFlag)
                {
                    sb.Append(" -z");
                }
            }
            else if (_tls is bool tls)
            {
                sb.Append($"tls={tls.ToString().ToLowerInvariant()}");
            }
        }

        // We ignore the Timeout and HasCompressionFlag properties when checking if two TCP endpoints are equivalent.
        protected internal override bool IsEquivalent(Endpoint? other) =>
            ReferenceEquals(this, other) ||
            (other is TcpEndpoint otherTcpEndpoint &&
                (_tls == otherTcpEndpoint._tls || _tls == null || otherTcpEndpoint._tls == null) && base.Equals(other));

        protected internal override void EncodeOptions11(IceEncoder encoder)
        {
            Debug.Assert(Protocol == Protocol.Ice1 && encoder.Encoding == Encoding.V11);
            base.EncodeOptions11(encoder);
            encoder.EncodeInt((int)_timeout.TotalMilliseconds);
            encoder.EncodeBool(_hasCompressionFlag);
        }

        // internal because it's used by some tests
        internal static TcpEndpoint CreateEndpoint(EndpointData data, Protocol protocol)
        {
            if (data.Options.Count > 0)
            {
                // Drop all options since we don't understand any.
                data = new EndpointData(data.Protocol, data.TransportName, data.TransportCode, data.Host, data.Port, ImmutableList<string>.Empty);
            }
            return new TcpEndpoint(data, protocol);
        }

        // Constructor for ice1 unmarshaling and parsing
        internal TcpEndpoint(EndpointData data, TimeSpan timeout, bool compress)
            : base(data, Protocol.Ice1)
        {
            _timeout = timeout;
            _hasCompressionFlag = compress;
            _tls = data.TransportCode == TransportCode.SSL;
        }

        // Constructor for unmarshaling with the 2.0 encoding.
        internal TcpEndpoint(EndpointData data, Protocol protocol)
            : base(data, protocol)
        {
            if (Protocol == Protocol.Ice1)
            {
                _tls = data.TransportCode == TransportCode.SSL;
            }
        }

        // Constructor for ice2 parsing.
        internal TcpEndpoint(EndpointData data, bool? tls)
            : base(data, Protocol.Ice2) =>
            _tls = tls;

        internal TcpEndpoint Clone(EndPoint address, bool tls)
        {
            if (address is IPEndPoint ipAddress)
            {
                string host = ipAddress.Address.ToString();
                ushort port = (ushort)ipAddress.Port;

                return (Host == host && Port == port && _tls == tls) ? this : new TcpEndpoint(this, host, port, tls);
            }
            else
            {
                throw new InvalidOperationException("unsupported address");
            }
        }

        // Clone constructor
        private TcpEndpoint(TcpEndpoint endpoint, string host, ushort port, bool? tls = null)
            : base(endpoint, host, port)
        {
            _hasCompressionFlag = endpoint._hasCompressionFlag;
            _timeout = endpoint._timeout;
            _tls = tls ?? endpoint._tls;
        }

        private TcpEndpoint Clone(ushort port) => port == Port ? this : new(this, Host, port);
    }

    internal abstract class TcpBaseEndpointFactory : IIce1EndpointFactory
    {
        public abstract string Name { get; }
        public abstract TransportCode TransportCode { get; }

        public Endpoint CreateEndpoint(EndpointData endpointData, Protocol protocol) =>
            TcpEndpoint.CreateEndpoint(endpointData, protocol);

        public Endpoint CreateIce1Endpoint(IceDecoder decoder)
        {
            Debug.Assert(TransportCode == TransportCode.TCP || TransportCode == TransportCode.SSL);

            // This is correct in C# since arguments are evaluated left-to-right. This would not be correct in C++
            // where the order of evaluation of function arguments is undefined.
            return new TcpEndpoint(new EndpointData(Protocol.Ice1,
                                                    TransportCode.ToString().ToLowerInvariant(),
                                                    TransportCode,
                                                    host: decoder.DecodeString(),
                                                    port: checked((ushort)decoder.DecodeInt()),
                                                    ImmutableList<string>.Empty),
                                   timeout: TimeSpan.FromMilliseconds(decoder.DecodeInt()),
                                   compress: decoder.DecodeBool());
        }

        public Endpoint CreateIce1Endpoint(Dictionary<string, string?> options, string endpointString)
        {
            Debug.Assert(TransportCode == TransportCode.TCP || TransportCode == TransportCode.SSL);

            (string host, ushort port) = Ice1Parser.ParseHostAndPort(options, endpointString);
            return new TcpEndpoint(new EndpointData(Protocol.Ice1, TransportCode.ToString().ToLowerInvariant(), TransportCode, host, port, ImmutableList<string>.Empty),
                                   Ice1Parser.ParseTimeout(options, TcpEndpoint.DefaultTimeout, endpointString),
                                   Ice1Parser.ParseCompress(options, endpointString));
        }
    }

    internal class TcpEndpointFactory : TcpBaseEndpointFactory, IIce2EndpointFactory
    {
        public ushort DefaultUriPort => IPEndpoint.DefaultUriPort;

        public override string Name => "tcp";

        public override TransportCode TransportCode => TransportCode.TCP;

        public Endpoint CreateIce2Endpoint(string host, ushort port, Dictionary<string, string> options)
        {
            bool? tls = null;
            if (options.TryGetValue("tls", out string? value))
            {
                tls = bool.Parse(value);
                options.Remove("tls");
            }
            return new TcpEndpoint(new EndpointData(Protocol.Ice2, "tcp", TransportCode.TCP, host, port, ImmutableList<string>.Empty), tls);
        }
    }

    internal class SslEndpointFactory : TcpBaseEndpointFactory
    {
        public override string Name => "ssl";

        public override TransportCode TransportCode => TransportCode.SSL;
    }
}
