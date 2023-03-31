// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Features;
using IceRpc.Slice;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;

namespace AuthorizationExample;

/// <summary>An Authenticator is an IceRPC service that implements the Slice interface 'Authenticator'.</summary>
internal class Authenticator : Service, IAuthenticatorService
{
    private readonly SigningCredentials _signingCredentials;

    public ValueTask<ReadOnlyMemory<byte>> AuthenticateAsync(
        string name,
        string password,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        var jwtToken = new JwtSecurityToken(
            claims: new Claim[]
            {
                 new Claim(JwtRegisteredClaimNames.Sub, "test"),
                 new Claim("isAdmin", (name == "admin").ToString())
            },
            audience: "Authorization example",
            issuer: "icerpc://127.0.0.1",
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow + TimeSpan.FromSeconds(30),
            signingCredentials: _signingCredentials);
        jwtToken.RawData

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
        return new(identityToken.Encrypt(_signingCredentials));
    }

    /// <summary>Constructs an authenticator service.</summary>
    /// <param name="signingCredentials">The credentials used to generate the identity token.</param>
    internal Authenticator(SigningCredentials signingCredentials) => _signingCredentials = signingCredentials;

}
