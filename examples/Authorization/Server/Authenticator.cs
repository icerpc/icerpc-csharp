// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;
using System.Security.Cryptography;

namespace AuthorizationExample;

/// <summary>The token store holds the session token to name dictionary and implements the
/// <see cref="ISessionManagerService" /> interface.</summary>
internal class Authenticator : Service, IAuthenticatorService
{
    private readonly SymmetricAlgorithm _cryptAlgorithm;

    public Authenticator(SymmetricAlgorithm cryptAlgorithm) => _cryptAlgorithm = cryptAlgorithm;

    public ValueTask<ReadOnlyMemory<byte>> AuthenticateAsync(
        string name,
        string password,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        if (password != "password")
        {
            throw new AuthenticationException("Invalid password.");
        }
        return new(new AuthenticationToken(name, isAdmin: name == "admin").Encrypt(_cryptAlgorithm));
    }
}
