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
    public IDuplexConnection CreateConnection(
        ServerAddress serverAddress,
        DuplexConnectionOptions options,
        SslClientAuthenticationOptions? clientAuthenticationOptions)
    {
        if ((serverAddress.Transport is string transport && !IsValidTransportName(transport, serverAddress.Protocol)) ||
            !CheckParams(serverAddress))
        {
            throw new ArgumentException(
                $"The server address '{serverAddress}' contains query parameters that are not valid for the Tcp client transport.",
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

        static bool IsValidTransportName(string transportName, Protocol protocol) =>
            protocol == Protocol.Ice ?
                transportName is TransportNames.Tcp or TransportNames.Ssl :
                transportName is TransportNames.Tcp;
    }

    /// <summary>Decodes the body of a tcp or ssl server address encoded using Slice1.</summary>
    internal static ServerAddress DecodeServerAddress(ref SliceDecoder decoder, string transport)
    {
        Debug.Assert(decoder.Encoding == SliceEncoding.Slice1);

        var body = new TcpServerAddressBody(ref decoder);

        if (Uri.CheckHostName(body.Host) == UriHostNameType.Unknown)
        {
            throw new InvalidDataException($"Received service address with invalid host '{body.Host}'.");
        }

        ImmutableDictionary<string, string> parameters = ImmutableDictionary<string, string>.Empty;
        if (body.Timeout != DefaultTcpTimeout)
        {
            parameters = parameters.Add("t", body.Timeout.ToString(CultureInfo.InvariantCulture));
        }
        if (body.Compress)
        {
            parameters = parameters.Add("z", "");
        }

        return new ServerAddress(Protocol.Ice, body.Host, checked((ushort)body.Port), transport, parameters);
    }

    /// <summary>Encodes the body of a tcp or ssl server address using Slice1.</summary>
    internal static void EncodeServerAddress(ref SliceEncoder encoder, ServerAddress serverAddress)
    {
        Debug.Assert(encoder.Encoding == SliceEncoding.Slice1);
        Debug.Assert(serverAddress.Protocol == Protocol.Ice);

        new TcpServerAddressBody(
            serverAddress.Host,
            serverAddress.Port,
            timeout: serverAddress.Params.TryGetValue("t", out string? timeoutValue) ?
                (timeoutValue == "infinite" ? -1 : int.Parse(timeoutValue, CultureInfo.InvariantCulture)) :
                DefaultTcpTimeout,
            compress: serverAddress.Params.ContainsKey("z")).Encode(ref encoder);
    }

    /// <summary>Checks if a server address has valid <see cref="ServerAddress.Params" />. Only the params are included
    /// in this check.</summary>
    /// <param name="serverAddress">The server address to check.</param>
    /// <returns><see langword="true" /> when all params of <paramref name="serverAddress" /> are valid; otherwise,
    /// <see langword="false" />.</returns>
    private static bool CheckParams(ServerAddress serverAddress)
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
}
