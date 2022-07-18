// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Net.Security;

namespace IceRpc.Transports;

/// <summary>A class to create a <see cref="IMultiplexedListener"/> to accept incoming multiplexed
/// connections.</summary>
public interface IMultiplexedServerTransport
{
    /// <summary>Gets the transport's name.</summary>
    string Name { get; }

    /// <summary>Starts listening on an endpoint.</summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="authenticationOptions">The SSL server authentication options.</param>
    /// <param name="logger">The logger created by IceRPC. IceRPC uses this logger to log calls to all Transport
    /// APIs it calls. The transport implementation can use this logger to log implementation-specific details
    /// within the log scopes created by IceRPC.</param>
    /// <returns>The new listener.</returns>
    IMultiplexedListener Listen(
        Endpoint endpoint,
        SslServerAuthenticationOptions? authenticationOptions,
        ILogger logger);
}
