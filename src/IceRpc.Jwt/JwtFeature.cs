// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IdentityModel.Tokens.Jwt;

namespace IceRpc.Jwt;

/// <summary>The default implementation of <see cref="IJwtFeature"/>.</summary>
public sealed class JwtFeature : IJwtFeature
{
    /// <inheritdoc/>
    public JwtSecurityToken Value { get; set; }

    /// <summary>Constructs a Jwt feature.</summary>
    /// <param name="value">The Jwt token.</param>
    public JwtFeature(JwtSecurityToken value) => Value = value;
}
