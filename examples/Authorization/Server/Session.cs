// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;
using IceRpc.Features;
using IceRpc.Slice;
using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace AuthorizationExample;

/// <summary>A feature that stores a user's name.</summary>
public interface ISessionFeature
{
    string Name { get; }
}

/// <summary> The implementation of <see cref="ISessionFeature" /></summary>
public class SessionFeature : ISessionFeature
{
    public string Name { get; }

    public SessionFeature(string name) => Name = name;
}

/// <summary> A comparer for byte arrays.</summary>
public class TokenComparer : IEqualityComparer<byte[]>
{
    public bool Equals(byte[]? x, byte[]? y) =>
        ReferenceEquals(x, y) || (x is not null && y is not null && x.SequenceEqual(y));

    public int GetHashCode(byte[] bytes) => BitConverter.ToInt32(bytes);
}

/// <summary> SessionManger holds the session token to name map.</summary>
public class TokenStore
{
    private readonly ConcurrentDictionary<byte[], string> _sessions = new(new TokenComparer());

    /// <summary>Create a new session token and store the name associated with it.</summary>
    /// <param name="name">The given name.</param>
    /// <returns>A new session token.</returns>
    public byte[] CreateToken(string name)
    {
        byte[] token = RandomNumberGenerator.GetBytes(128);
        _sessions[token] = name;
        return token;
    }

    /// <summary>Gets the name associated with the given session token.</summary>
    /// <param name="token">The session token</param>
    /// <returns>The name.</returns>
    public string? GetName(byte[] token) => _sessions.TryGetValue(token, out string? name) ? name : null;
}

/// <summary>Middleware that loads the session token from the request and adds the session feature to the request's
/// feature collection.</summary>
public class LoadSessionMiddleware : IDispatcher
{
    private readonly IDispatcher _next;

    private readonly TokenStore _tokenStore;

    public LoadSessionMiddleware(IDispatcher next, TokenStore tokenStore)
    {
        _next = next;
        _tokenStore = tokenStore;
    }

    public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken)
    {
        if (request.Fields.TryGetValue(SessionFieldKey.Value, out ReadOnlySequence<byte> value))
        {
            byte[] token = value.ToArray();
            if (_tokenStore.GetName(token) is string name)
            {
                request.Features = request.Features.With<ISessionFeature>(new SessionFeature(name));
            }
        }
        return _next.DispatchAsync(request, cancellationToken);
    }
}

/// <summary>Middleware that checks if the request has a session feature. If not, it throws a
/// <see cref="DispatchException" />.</summary>
public class HasSessionMiddleware : IDispatcher
{
    private readonly IDispatcher _next;

    public HasSessionMiddleware(IDispatcher next) => _next = next;
    public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken)
    {
        if (request.Features.Get<ISessionFeature>() is null)
        {
            throw new DispatchException(StatusCode.Unauthorized, "Not authorized.");
        }
        return _next.DispatchAsync(request, cancellationToken);
    }
}

/// <summary>The implementation of the <see cref="ISessionManager" /> interface.</summary>
public class SessionManager : Service, ISessionManager
{
    private readonly TokenStore _tokenStore;

    public SessionManager(TokenStore tokenStore) => _tokenStore = tokenStore;

    public ValueTask<ReadOnlyMemory<byte>> CreateSessionAsync(
        string name,
        IFeatureCollection features,
        CancellationToken cancellationToken) => new(_tokenStore.CreateToken(name));
}
