// Copyright (c) ZeroC, Inc. All rights reserved.

using System.IdentityModel.Tokens.Jwt;

namespace IceRpc.Jwt;

/// <summary>A feature that represents a Jwt token that can be transmitted in an IceRPC field.</summary>
public interface IJwtFeature
{
    /// <summary>Gets or sets the value of this JWT feature.</summary>
    JwtSecurityToken Value { get; set; }
}
