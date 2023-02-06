// Copyright (c) ZeroC, Inc.

using IceRpc.Slice;
using IceRpc.Transports.Internal;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Net.Security;

namespace IceRpc.Transports;

/// <summary>Implements <see cref="IDuplexClientTransport" /> for the tcp and ssl transports.</summary>
public class TcpClientTransport : IDuplexClientTransport
{
    /// <inheritdoc/>
    public string Name => TransportNames.Tcp;

    /// <summary>The default timeout value for tcp/ssl server addresses with Slice1.</summary>
    private const int DefaultTcpTimeout = 60_000; // 60s

    private readonly TcpClientTransportOptions _options;

    /// <summary>Constructs a <see cref="TcpClientTransport" />.</summary>
    public TcpClientTransport()
        : this(new TcpClientTransportOptions())
    {
    }

    /// <summary>Constructs a <see cref="TcpClientTransport" />.</summary>
    /// <param name="options">The transport options.</param>
    public TcpClientTransport(TcpClientTransportOptions options) => _options = options;

    /// <inheritdoc/>
    public bool CheckParams(ServerAddress serverAddress)
    {
        if (serverAddress.Protocol != Protocol.Ice)
        {
            return serverAddress.Params.Count == 0;
        }
        else
        {
            foreach (string name in serverAddress.Params.Keys)
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
    public IDuplexConnection CreateConnection(
        ServerAddress serverAddress,
        DuplexConnectionOptions options,
        SslClientAuthenticationOptions? clientAuthenticationOptions)
    {
        if ((serverAddress.Transport is string transport &&
            transport != TransportNames.Tcp &&
            transport != TransportNames.Ssl) ||
            !CheckParams(serverAddress))
        {
            throw new ArgumentException(
                $"The server address contains parameters that are not valid for the Tcp client transport: '{serverAddress}'.",
                nameof(serverAddress));
        }

        if (serverAddress.Transport is null)
        {
            serverAddress = serverAddress with { Transport = Name };
        }

        SslClientAuthenticationOptions? authenticationOptions = clientAuthenticationOptions?.Clone() ??
            (serverAddress.Transport == TransportNames.Ssl ? new SslClientAuthenticationOptions() : null);

        if (authenticationOptions is not null)
        {
            // We are establishing a secure TLS connection. It can rely on system certificates.

            authenticationOptions.TargetHost ??= serverAddress.Host;

            // Set ApplicationProtocols to "ice" or "icerpc" in the common situation where the application does not
            // specify any application protocol. This way, a proxy server listening on a port shared by multiple
            // application protocols can use this ALPN protocol ID to forward all ice/icerpc traffic to an ice/icerpc
            // back-end server.
            // We do this only when the port is not the default port for ice or icerpc; when we use the IANA-registered
            // default port, the server can and should use this port number to identify the application protocol when no
            // ALPN protocol ID is provided.
            if (authenticationOptions.ApplicationProtocols is null &&
                serverAddress.Port != serverAddress.Protocol.DefaultPort)
            {
                authenticationOptions.ApplicationProtocols = new List<SslApplicationProtocol>
                {
                    new SslApplicationProtocol(serverAddress.Protocol.Name)
                };
            }
        }

        return new TcpClientConnection(
            serverAddress,
            authenticationOptions,
            options.Pool,
            options.MinSegmentSize,
            _options);
    }

    /// <summary>Decodes the body of a tcp or ssl ice server address encoded using Slice1.</summary>
    internal static ServerAddress DecodeServerAddress(ref SliceDecoder decoder, string transport)
    {
        Debug.Assert(decoder.Encoding == SliceEncoding.Slice1);

        string host = decoder.DecodeString();
        if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            throw new InvalidDataException($"Received service address with invalid host '{host}'.");
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

        return new ServerAddress(Protocol.Ice, host, port, transport, parameters);
    }

    /// <summary>Encodes the body of a tcp or ssl ice server address using Slice1.</summary>
    internal static void EncodeServerAddress(ref SliceEncoder encoder, ServerAddress serverAddress)
    {
        Debug.Assert(encoder.Encoding == SliceEncoding.Slice1);
        Debug.Assert(serverAddress.Protocol == Protocol.Ice);

        encoder.EncodeString(serverAddress.Host);
        encoder.EncodeInt32(serverAddress.Port);
        int timeout = serverAddress.Params.TryGetValue("t", out string? timeoutValue) ?
            timeoutValue == "infinite" ? -1 : int.Parse(timeoutValue, CultureInfo.InvariantCulture) :
            DefaultTcpTimeout;
        encoder.EncodeInt32(timeout);
        encoder.EncodeBool(serverAddress.Params.ContainsKey("z"));
    }
}
