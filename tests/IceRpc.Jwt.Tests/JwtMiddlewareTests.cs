// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;
using IceRpc.Tests.Common;
using Microsoft.IdentityModel.Tokens;
using NUnit.Framework;
using System.Buffers;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace IceRpc.Jwt.Tests;

public class JwtMiddlewareTests
{
    [Test]
    public async Task Jwt_middleware_encodes_the_token_from_the_jwt_feature()
    {
        // Arrange
        var tokenHandler = new JwtSecurityTokenHandler();
        var issuerKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("A dummy secret key for Jwt test"));
        var credentials = new SigningCredentials(issuerKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            claims: new Claim[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, "test"),
            },
            audience: "Jwt Test",
            issuer: "icerpc://127.0.0.1",
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow + TimeSpan.FromSeconds(5),
            signingCredentials: credentials);

        var dispatcher = new InlineDispatcher((request, cancel) =>
        {
            request.Features = request.Features.With<IJwtFeature>(new JwtFeature(token));
            var response = new OutgoingResponse(request);
            return new(response);
        });
        var sut = new JwtMiddleware(dispatcher);

        var request = new IncomingRequest(InvalidConnection.IceRpc)
        {
            Operation = "Op",
            Path = "/"
        };

        // Act
        OutgoingResponse response = await sut.DispatchAsync(request, CancellationToken.None);

        // Assert
        Assert.That(response.Fields.ContainsKey(ResponseFieldKey.Jwt), Is.True);
        Assert.That(
            JwtTestUtils.DecodeJwtField(response.Fields[ResponseFieldKey.Jwt]),
            Is.EqualTo(tokenHandler.WriteToken(token)));
    }

    [Test]
    public async Task JWt_middleware_decodes_the_jwt_field_into_jwt_feature()
    {
        // Arrange
        var tokenHandler = new JwtSecurityTokenHandler();
        var issuerKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("A dummy secret key for Jwt test"));
        var credentials = new SigningCredentials(issuerKey, SecurityAlgorithms.HmacSha256);

        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            ValidateAudience = true,
            ValidateIssuer = true,
            ValidateLifetime = true,
            RequireSignedTokens = true,
            ValidAudience = "Jwt Test",
            ValidIssuer = "icerpc://127.0.0.1",
            IssuerSigningKey = issuerKey,
            ClockSkew = TimeSpan.FromSeconds(5),
        };

        var token = new JwtSecurityToken(
            claims: new Claim[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, "test"),
            },
            audience: "Jwt Test",
            issuer: "icerpc://127.0.0.1",
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow + TimeSpan.FromSeconds(5),
            signingCredentials: credentials);

        var request = new IncomingRequest(InvalidConnection.IceRpc)
        {
            Operation = "Op",
            Path = "/",
            Fields = new Dictionary<RequestFieldKey, ReadOnlySequence<byte>>
            {
                [RequestFieldKey.Jwt] = JwtTestUtils.EncodeJwtToken(tokenHandler.WriteToken(token))
            }
        };

        IJwtFeature? feature = null;
        var dispatcher = new InlineDispatcher((request, cancel) =>
        {
            feature = request.Features.Get<IJwtFeature>();
            var response = new OutgoingResponse(request);
            return new(response);
        });
        var sut = new JwtMiddleware(dispatcher, tokenValidationParameters);

        // Act
        OutgoingResponse response = await sut.DispatchAsync(request, CancellationToken.None);

        // Assert
        Assert.That(feature, Is.Not.Null);
        Assert.That(token.EncodedPayload, Is.EqualTo(feature.Token.EncodedPayload));
    }

    [Test]
    public void JWt_middleware_refuses_invalid_token_with_invalid_creadentials()
    {
        // Arrange
        var tokenHandler = new JwtSecurityTokenHandler();
        var issuerKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("A dummy secret key for Jwt test"));
        var credentials = new SigningCredentials(issuerKey, SecurityAlgorithms.HmacSha256);

        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            ValidateAudience = true,
            ValidateIssuer = true,
            ValidateLifetime = true,
            RequireSignedTokens = true,
            ValidAudience = "Jwt Test",
            ValidIssuer = "icerpc://127.0.0.1",
            IssuerSigningKey = issuerKey,
            ClockSkew = TimeSpan.FromSeconds(5),
        };

        // Invalid the audience doesn't match
        var token = new JwtSecurityToken(
            claims: new Claim[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, "test"),
            },
            audience: "Jwt Test Invalid",
            issuer: "icerpc://127.0.0.1",
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow + TimeSpan.FromSeconds(5),
            signingCredentials: credentials);

        var request = new IncomingRequest(InvalidConnection.IceRpc)
        {
            Operation = "Op",
            Path = "/",
            Fields = new Dictionary<RequestFieldKey, ReadOnlySequence<byte>>
            {
                [RequestFieldKey.Jwt] = JwtTestUtils.EncodeJwtToken(tokenHandler.WriteToken(token))
            }
        };

        IJwtFeature? feature = null;
        var dispatcher = new InlineDispatcher((request, cancel) =>
        {
            feature = request.Features.Get<IJwtFeature>();
            var response = new OutgoingResponse(request);
            return new(response);
        });
        var sut = new JwtMiddleware(dispatcher, tokenValidationParameters);

        // Act//Assert
        DispatchException ex = Assert.CatchAsync<DispatchException>(
            async () => await sut.DispatchAsync(request, CancellationToken.None));
        Assert.That(ex.ErrorCode, Is.EqualTo(DispatchErrorCode.InvalidCredentials));
    }
}
