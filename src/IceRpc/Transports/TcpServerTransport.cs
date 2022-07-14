// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System.Net.Security;

namespace IceRpc.Transports;

/// <summary>Implements <see cref="IServerTransport{ISingleStreamTransportConnection}"/> for the tcp and ssl transports.
/// </summary>
public class TcpServerTransport : IServerTransport<ISingleStreamTransportConnection>
{
    /// <inheritdoc/>
    public string Name => TransportNames.Tcp;

    private readonly TcpServerTransportOptions _options;

    /// <summary>Constructs a <see cref="TcpServerTransport"/>.</summary>
    public TcpServerTransport()
        : this(new())
    {
    }

    /// <summary>Constructs a <see cref="TcpServerTransport"/>.</summary>
    /// <param name="options">The transport options.</param>
    public TcpServerTransport(TcpServerTransportOptions options) => _options = options;

    /// <inheritdoc/>
    public IListener<ISingleStreamTransportConnection> Listen(
        Endpoint endpoint,
        SslServerAuthenticationOptions? authenticationOptions,
        ILogger logger)
    {
        // This is the composition root of the tcp server transport, where we install log decorators when logging
        // is enabled.

        if (TcpClientTransport.CheckParams(endpoint, out string? endpointTransport))
        {
            if (endpointTransport is null)
            {
                endpoint = endpoint with { Params = endpoint.Params.Add("transport", Name) };
            }
            else if (endpointTransport == TransportNames.Ssl && authenticationOptions is null)
            {
                throw new ArgumentNullException(
                    nameof(authenticationOptions),
                    $"{nameof(authenticationOptions)} cannot be null with the ssl transport");
            }
        }
        else
        {
            throw new FormatException($"cannot create a TCP listener for endpoint '{endpoint}'");
        }

        Func<TcpServerTransportConnection, ISingleStreamTransportConnection> serverConnectionDecorator =
            logger.IsEnabled(TcpLoggerExtensions.MaxLogLevel) ?
                connection => new LogTcpTransportConnectionDecorator(connection, logger) : connection => connection;

        return new TcpListener(endpoint, authenticationOptions, _options, serverConnectionDecorator);
    }
}
