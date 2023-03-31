// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Features;
using IceRpc.Slice;

namespace AuthorizationExample;

/// <summary>An Authenticator is an IceRPC service that implements the Slice interface 'Authenticator'.</summary>
internal class Authenticator : Service, IAuthenticatorService
{
    private readonly IAuthenticationBearer _authenticationBearer;

    public ValueTask<ReadOnlyMemory<byte>> AuthenticateAsync(
        string name,
        string password,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        // Check if the user name and password are valid.
        bool isAdmin;
        if (name == "admin" && password == "admin-password")
        {
            isAdmin = true;
        }
        else if (name == "friend" && password == "password")
        {
            isAdmin = false;
        }
        else
        {
            throw new DispatchException(StatusCode.Unauthorized, "Unknown user or invalid password.");
        }

        // Return the encrypted identity token.
        return new(_authenticationBearer.EncodeIdentityToken(name, isAdmin));
    }

    /// <summary>Constructs an authenticator service.</summary>
    /// <param name="authenticationBearer">The authentication bearer to encode an identity token.</param>
    internal Authenticator(IAuthenticationBearer authenticationBearer) => _authenticationBearer = authenticationBearer;

}
