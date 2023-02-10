// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;
using System.Collections.Concurrent;

namespace AuthorizationExample;

/// <summary>A feature that stores a user's name.</summary>
public interface ISessionFeature
{
    string Name { get; }
}

/// <summary>The implementation of <see cref="ISessionFeature" />.</summary>
internal class SessionFeature : ISessionFeature
{
    public string Name { get; }

    public SessionFeature(string name) => Name = name;
}

/// <summary>The TokenStore holds the session token to name map.</summary>
internal class TokenStore
{
    private readonly ConcurrentDictionary<Guid, string> _sessions = new();

    /// <summary>Creates a new session token and stores the name associated with it.</summary>
    /// <param name="name">The given name.</param>
    /// <returns>A new session token.</returns>
    internal Guid CreateToken(string name)
    {
        // Guid are not cryptographically secure, but for this example it's sufficient.
        var token = Guid.NewGuid();
        _sessions[token] = name;
        return token;
    }

    /// <summary>Gets the name associated with the given session token.</summary>
    /// <param name="token">The session token</param>
    /// <returns>The name.</returns>
    internal string? GetName(Guid token) => _sessions.TryGetValue(token, out string? name) ? name : null;
}

/// <summary>The implementation of the <see cref="ISessionManagerService" /> interface.</summary>
internal class SessionManagerService : Service, ISessionManagerService
{
    private readonly TokenStore _tokenStore;

    internal SessionManagerService(TokenStore tokenStore) => _tokenStore = tokenStore;

    public ValueTask<ReadOnlyMemory<byte>> CreateSessionAsync(
        string name,
        IFeatureCollection features,
        CancellationToken cancellationToken) => new(_tokenStore.CreateToken(name).ToByteArray());
}
