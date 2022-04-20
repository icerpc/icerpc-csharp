// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Net.Security;

namespace IceRpc.Transports
{
    /// <summary>Implements <see cref="IClientTransport{ISimpleNetworkConnection}"/> for the tcp and ssl transports.
    /// </summary>
    public class TcpClientTransport : IClientTransport<ISimpleNetworkConnection>
    {
        /// <inheritdoc/>
        public string Name => TransportNames.Tcp;

        /// <summary>The default timeout value for tcp/ssl endpoints with Slice1.</summary>
        private const int DefaultTcpTimeout = 60_000; // 60s

        private readonly TcpClientTransportOptions _options;

        /// <summary>Constructs a <see cref="TcpClientTransport"/>.</summary>
        public TcpClientTransport()
            : this(new())
        {
        }

        /// <summary>Constructs a <see cref="TcpClientTransport"/>.</summary>
        /// <param name="options">The transport options.</param>
        public TcpClientTransport(TcpClientTransportOptions options) => _options = options;

        /// <inheritdoc/>
        public ISimpleNetworkConnection CreateConnection(
            Endpoint remoteEndpoint,
            SslClientAuthenticationOptions? authenticationOptions,
            ILogger logger)
        {
            // This is the composition root of the tcp client transport, where we install log decorators when logging
            // is enabled.

            if (!CheckEndpointParams(remoteEndpoint.Params, out string? remoteEndpointTransport))
            {
                throw new FormatException($"cannot create a TCP connection to endpoint '{remoteEndpoint}'");
            }

            authenticationOptions = authenticationOptions?.Clone() ??
                (remoteEndpointTransport == TransportNames.Ssl ? new SslClientAuthenticationOptions() : null);

            if (authenticationOptions != null)
            {
                // Add the endpoint protocol to the SSL application protocols (used by TLS ALPN) and set the
                // TargetHost to the endpoint host. On the client side, the application doesn't necessarily
                // need to provide authentication options if it relies on system certificates and doesn't specify
                // certificate validation.
                authenticationOptions.TargetHost ??= remoteEndpoint.Host;
                authenticationOptions.ApplicationProtocols ??= new List<SslApplicationProtocol>
                {
                    new SslApplicationProtocol(remoteEndpoint.Protocol.Name)
                };
            }

            var clientConnection = new TcpClientNetworkConnection(
                remoteEndpoint.Host,
                remoteEndpoint.Port,
                authenticationOptions,
                _options);

            return logger.IsEnabled(TcpLoggerExtensions.MaxLogLevel) ?
                new LogTcpNetworkConnectionDecorator(clientConnection, logger) : clientConnection;
        }

        /// <summary>Checks the parameters of a tcp endpoint and returns the value of the transport parameter. The "t"
        /// and "z" parameters are supported and ignored for compatibility with ZeroC Ice.</summary>
        /// <returns><c>true</c> when the endpoint parameters are valid; otherwise, <c>false</c>.</returns>
        internal static bool CheckEndpointParams(
            ImmutableDictionary<string, string> endpointParams,
            out string? transportValue)
        {
            transportValue = null;

            foreach ((string name, string value) in endpointParams)
            {
                switch (name)
                {
                    case "transport":
                        if (value is TransportNames.Tcp or TransportNames.Ssl)
                        {
                            transportValue = value;
                        }
                        else
                        {
                            return false;
                        }
                        break;

                    case "t":
                    case "z":
                        // we don't check the value since we ignore it
                        break;

                    default:
                        return false;
                }
            }

            return true;
        }

        /// <summary>Decodes the body of a tcp or ssl ice endpoint encoded using Slice1.</summary>
        internal static Endpoint DecodeEndpoint(ref SliceDecoder decoder, string transport)
        {
            Debug.Assert(decoder.Encoding == SliceEncoding.Slice1);

            string host = decoder.DecodeString();
            if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
            {
                throw new InvalidDataException($"received proxy with invalid host '{host}'");
            }

            ushort port = checked((ushort)decoder.DecodeInt32());
            int timeout = decoder.DecodeInt32();
            bool compress = decoder.DecodeBool();

            ImmutableDictionary<string, string>.Builder builder =
                ImmutableDictionary.CreateBuilder<string, string>();

            builder.Add("transport", transport);

            if (timeout != DefaultTcpTimeout)
            {
                builder.Add("t", timeout.ToString(CultureInfo.InvariantCulture));
            }
            if (compress)
            {
                builder.Add("z", "");
            }

            return new Endpoint(Protocol.Ice, host, port, builder.ToImmutable());
        }

        /// <summary>Encodes the body of a tcp or ssl ice endpoint using Slice1.</summary>
        internal static void EncodeEndpoint(ref SliceEncoder encoder, Endpoint endpoint)
        {
            Debug.Assert(encoder.Encoding == SliceEncoding.Slice1);
            Debug.Assert(endpoint.Protocol == Protocol.Ice);

            encoder.EncodeString(endpoint.Host);
            encoder.EncodeInt32(endpoint.Port);
            int timeout = endpoint.Params.TryGetValue("t", out string? timeoutValue) ?
                timeoutValue == "infinite" ? -1 : int.Parse(timeoutValue, CultureInfo.InvariantCulture) :
                DefaultTcpTimeout;
            encoder.EncodeInt32(timeout);
            encoder.EncodeBool(endpoint.Params.ContainsKey("z"));
        }
    }
}
