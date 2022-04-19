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

            string? endpointTransport = ParseEndpointParams(remoteEndpoint);

            if (endpointTransport == null)
            {
                remoteEndpoint = remoteEndpoint with { Params = remoteEndpoint.Params.Add("transport", Name) };
            }
            else if (endpointTransport == TransportNames.Ssl)
            {
                // With ssl, we always "turn on" SSL
                authenticationOptions ??= new SslClientAuthenticationOptions();
            }

            var clientConnection = new TcpClientNetworkConnection(
                remoteEndpoint,
                authenticationOptions,
                _options);

            return logger.IsEnabled(TcpLoggerExtensions.MaxLogLevel) ?
                new LogTcpNetworkConnectionDecorator(clientConnection, logger) : clientConnection;
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

        /// <summary>Checks the parameters of a tcp endpoint and returns the value of the transport parameter. The "t"
        /// and "z" parameters are supported and ignored for compatibility with ZeroC Ice.</summary>
        /// <returns>The value of the transport parameter, or null if the transport parameter is not set.</returns>
        /// <exception cref="FormatException">Thrown when an endpoint parameter is unknown or transport has an invalid
        /// value.</exception>
        internal static string? ParseEndpointParams(Endpoint endpoint)
        {
            string? transportValue = null;

            foreach ((string name, string value) in endpoint.Params)
            {
                switch (name)
                {
                    case "transport":
                        transportValue = value switch
                        {
                            TransportNames.Tcp or TransportNames.Ssl => value,
                            _ => throw new FormatException(
                                $"invalid value for transport parameter in endpoint '{endpoint}'")
                        };
                        break;

                    case "t":
                    case "z":
                        // we don't check the value since we ignore it
                        break;

                    default:
                        throw new FormatException($"unknown parameter '{name}' in endpoint '{endpoint}'");
                }
            }

            return transportValue;
        }
    }
}
