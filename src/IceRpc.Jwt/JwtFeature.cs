// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IdentityModel.Tokens.Jwt;

namespace IceRpc.Jwt;

/// <summary>The default implementation of <see cref="IJwtFeature"/>.</summary>
public sealed class JwtFeature : IJwtFeature
{
    /// <inheritdoc/>
    public JwtSecurityToken Token { get; set; }

    /// <summary>Constructs a Jwt feature.</summary>
    /// <param name="token">The Jwt token.</param>
    public JwtFeature(JwtSecurityToken token) => Token = token;
}
