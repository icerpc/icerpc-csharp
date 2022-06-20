// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.IdentityModel.Tokens;
using NUnit.Framework;
using System.Buffers;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace IceRpc.Jwt.Tests;

public class JwtInterceptorTests
{
    [Test]
    public async Task Jwt_interceptor_decodes_the_jwt_token_in_the_response_field()
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
        bool called = false;
        string? decodedTokendField = null;
        var invoker = new InlineInvoker((request, cancel) =>
        {
            IncomingResponse response = called ?
                new IncomingResponse(request, request.Connection!) :
                new IncomingResponse(
                    request,
                    request.Connection!,
                    new Dictionary<ResponseFieldKey, ReadOnlySequence<byte>>
                    {
                        [ResponseFieldKey.Jwt] = JwtTestUtils.EncodeJwtToken(tokenHandler.WriteToken(token))
                    });
            if (called)
            {
                decodedTokendField = JwtTestUtils.DecodeJwtField(request.Fields[RequestFieldKey.Jwt]);
            }
            called = true;
            return Task.FromResult(response);
        });
        var sut = new JwtInterceptor(invoker);

        var request = new OutgoingRequest(new Proxy(Protocol.IceRpc) { Path = "/" })
        {
            Operation = "op"
        };
        IncomingResponse response = await sut.InvokeAsync(request, CancellationToken.None);

        // Act
        response = await sut.InvokeAsync(request, CancellationToken.None);

        // Assert
        Assert.That(decodedTokendField, Is.EqualTo(tokenHandler.WriteToken(token)));
    }
}
