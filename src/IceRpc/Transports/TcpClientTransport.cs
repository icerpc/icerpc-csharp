// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Transports.Internal;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Net.Security;

namespace IceRpc.Transports;

/// <summary>Implements <see cref="IDuplexClientTransport"/> for the tcp and ssl transports.</summary>
public class TcpClientTransport : IDuplexClientTransport
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
    public bool CheckParams(Endpoint endpoint)
    {
        if (endpoint.Protocol != Protocol.Ice)
        {
            return endpoint.Params.Count == 0;
        }
        else
        {
            foreach (string name in endpoint.Params.Keys)
            {
                switch (name)
                {
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
    }

    /// <inheritdoc/>
    public IDuplexConnection CreateConnection(DuplexClientConnectionOptions options)
    {
        // This is the composition root of the tcp client transport, where we install log decorators when logging
        // is enabled.
        Endpoint endpoint = options.Endpoint;
        if ((endpoint.Transport is string transport &&
            transport != TransportNames.Tcp &&
            transport != TransportNames.Ssl) ||
            !CheckParams(endpoint))
        {
            throw new FormatException($"cannot create a TCP connection to endpoint '{endpoint}'");
        }

        if (endpoint.Transport is null)
        {
            endpoint = endpoint with { Transport = Name };
        }

        SslClientAuthenticationOptions? authenticationOptions = options.ClientAuthenticationOptions?.Clone() ??
            (endpoint.Transport == TransportNames.Ssl ? new SslClientAuthenticationOptions() : null);
        if (authenticationOptions is not null)
        {
            // Add the endpoint protocol to the SSL application protocols (used by TLS ALPN) and set the
            // TargetHost to the endpoint host. On the client side, the application doesn't necessarily
            // need to provide authentication options if it relies on system certificates and doesn't specify
            // certificate validation.
            authenticationOptions.TargetHost ??= endpoint.Host;
            authenticationOptions.ApplicationProtocols ??= new List<SslApplicationProtocol>
            {
                new SslApplicationProtocol(endpoint.Protocol.Name)
            };
        }

        return new TcpClientConnection(
            endpoint,
            authenticationOptions,
            options.Pool,
            options.MinSegmentSize,
            _options);
    }

    /// <summary>Decodes the body of a tcp or ssl ice endpoint encoded using Slice1.</summary>
    internal static Endpoint DecodeEndpoint(ref SliceDecoder decoder, string transport)
    {
        Debug.Assert(decoder.Encoding == SliceEncoding.Slice1);

        string host = decoder.DecodeString();
        if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            throw new InvalidDataException($"received service address with invalid host '{host}'");
        }

        ushort port = checked((ushort)decoder.DecodeInt32());
        int timeout = decoder.DecodeInt32();
        bool compress = decoder.DecodeBool();

        ImmutableDictionary<string, string> parameters = ImmutableDictionary<string, string>.Empty;
        if (timeout != DefaultTcpTimeout)
        {
            parameters = parameters.Add("t", timeout.ToString(CultureInfo.InvariantCulture));
        }
        if (compress)
        {
            parameters = parameters.Add("z", "");
        }

        return new Endpoint(Protocol.Ice, host, port, transport, parameters);
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
