// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Jwt;
using IceRpc.Slice;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Demo;

public class Auth : Service, IAuth
{
    private readonly SigningCredentials _credentials;

    public Auth(SigningCredentials credentials) => _credentials = credentials;

    public ValueTask SignInAsync(string name, string password, IFeatureCollection features, CancellationToken cancel)
    {
        if (name.ToLowerInvariant() == password)
        {
            var token = new JwtSecurityToken(
                claims: new Claim[]
                {
                    new Claim(JwtRegisteredClaimNames.Sub, name),
                },
                audience: "Jwt Example",
                issuer: "icerpc://127.0.0.1",
                notBefore: DateTime.UtcNow,
                expires: DateTime.UtcNow + TimeSpan.FromSeconds(5),
                signingCredentials: _credentials);
            features.Set<IJwtFeature>(new JwtFeature(token));
        }
        return default;
    }
}
