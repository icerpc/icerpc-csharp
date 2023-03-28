// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Features;
using IceRpc.Slice;
using System.Security.Cryptography;

namespace AuthorizationExample;

/// <summary>An Authenticator is an IceRPC service that implements the Slice interface 'Authenticator'.</summary>
internal class Authenticator : Service, IAuthenticatorService
{
    private readonly SymmetricAlgorithm _encryptionAlgorithm;

    public ValueTask<ReadOnlyMemory<byte>> AuthenticateAsync(
        string name,
        string password,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        if (password != "password")
        {
            throw new DispatchException(StatusCode.Unauthorized, "Invalid password.");
        }
        return new(new AuthenticationToken(name, isAdmin: name == "admin").Encrypt(_encryptionAlgorithm));
    }

    /// <summary>Constructs an authenticator service.</summary>
    /// <param name="encryptionAlgorithm">The encryption algorithm used to encrypt an authentication token.</param>
    internal Authenticator(SymmetricAlgorithm encryptionAlgorithm) => _encryptionAlgorithm = encryptionAlgorithm;

}
