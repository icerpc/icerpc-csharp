// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;
using IceRpc.Features;
using IceRpc.Slice;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace AuthorizationExample;

public interface ISessionFeature
{
    string Name { get; }
}

public struct SessionFeature : ISessionFeature
{
    public string Name { get; }

    public SessionFeature(string name) => Name = name;

}

public class SessionTokenComparer : IEqualityComparer<byte[]>
{
    public bool Equals(byte[]? x, byte[]? y)
    {
        if (x is null || y is null)
        {
            return false;
        }

        if (x.Length != y.Length)
        {
            return false;
        }

        for (int i = 0; i < x.Length; i++)
        {
            if (x[i] != y[i])
            {
                return false;
            }
        }
        return true;
    }

    public int GetHashCode([DisallowNull] byte[] bytes) => BitConverter.ToInt32(bytes);
}

public class SessionManager
{
    private readonly ConcurrentDictionary<byte[], string> _sessions = new(new SessionTokenComparer());

    public byte[] CreateSession(string name)
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(128);
        _sessions[bytes] = name;
        return bytes;
    }

    public string? GetName(byte[] bytes) => _sessions.TryGetValue(bytes, out string? name) ? name : null;

    public IDispatcher LoadSession(IDispatcher next)
    {
        return new InlineDispatcher((request, cancellationToken) =>
        {
            if (request.Fields.TryGetValue((RequestFieldKey)100, out ReadOnlySequence<byte> value))
            {
                byte[] token = value.ToArray();
                if (GetName(token) is string name)
                {
                    request.Features = request.Features.With<ISessionFeature>(new SessionFeature(name));
                }
            }
            return next.DispatchAsync(request, cancellationToken);
        });
    }

    public static IDispatcher HasSession(IDispatcher next)
    {
        return new InlineDispatcher((request, cancellationToken) =>
        {
            if (request.Features.Get<ISessionFeature>() is null)
            {
                throw new DispatchException(StatusCode.Unauthorized, "Not authorized");
            }
            return next.DispatchAsync(request, cancellationToken);
        });
    }
}

public class SessionService : Service, ISession
{
    private readonly SessionManager _sessionManager;

    public SessionService(SessionManager sessionManager) => _sessionManager = sessionManager;

    public ValueTask<ReadOnlyMemory<byte>> LoginAsync(
        string name, IFeatureCollection features, CancellationToken cancellationToken)
        => new(_sessionManager.CreateSession(name));
}
