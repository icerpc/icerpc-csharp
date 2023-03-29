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
        // Check if the user name and password are valid.
        IdentityToken identityToken;
        if (name == "admin" && password == "admin-password")
        {
            identityToken = new IdentityToken(isAdmin: true, name);
        }
        else if (name == "friend" && password == "password")
        {
            identityToken = new IdentityToken(isAdmin: false, name);
        }
        else
        {
            throw new DispatchException(StatusCode.Unauthorized, "Unknown user or invalid password.");
        }

        // Return the encrypted identity token.
        return new(identityToken.Encrypt(_encryptionAlgorithm));
    }

    /// <summary>Constructs an authenticator service.</summary>
    /// <param name="encryptionAlgorithm">The encryption algorithm used to encrypt an identity token.</param>
    internal Authenticator(SymmetricAlgorithm encryptionAlgorithm) => _encryptionAlgorithm = encryptionAlgorithm;

}
