// Copyright (c) ZeroC, Inc.

using IceRpc;
using Microsoft.IdentityModel.Tokens;
using System.Buffers;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace AuthorizationExample;

/// <summary>This is an implementation of the <see cref="IAuthenticationBearer" /> using JSON Web Token (JWT).</summary>
internal sealed class JwtAuthenticationBearer : IAuthenticationBearer
{
    private readonly SigningCredentials _signingCredentials;
    private readonly JwtSecurityTokenHandler _tokenHandler;
    private readonly TokenValidationParameters _tokenValidationParameters;

    public async Task<IIdentityFeature> DecodeAndValidateIdentityTokenAsync(ReadOnlySequence<byte> identityTokenBytes)
    {
        string jwtTokenString = Encoding.UTF8.GetString(identityTokenBytes.ToArray());
        TokenValidationResult validationResult = await _tokenHandler.ValidateTokenAsync(
            jwtTokenString,
            _tokenValidationParameters);
        if (validationResult.IsValid)
        {
            var jwtToken = (JwtSecurityToken)validationResult.SecurityToken;

            string name;
            IEnumerable<Claim> subjectClaim = jwtToken.Claims.Where(claim => claim.Type == JwtRegisteredClaimNames.Sub);
            if (!subjectClaim.Any())
            {
                throw new DispatchException(StatusCode.Unauthorized, "The JWT token is missing the subject claim.");
            }
            name = subjectClaim.First().Value;

            bool isAdmin = false;
            IEnumerable<Claim> isAdminClaim = jwtToken.Claims.Where(claim => claim.Type == "isAdmin");
            if (!isAdminClaim.Any())
            {
                throw new DispatchException(StatusCode.Unauthorized, "The JWT token is missing the isAdmin claim.");
            }
            else if (!bool.TryParse(isAdminClaim.First().Value, out isAdmin))
            {
                throw new DispatchException(StatusCode.Unauthorized, "The JWT token isAdmin claim is invalid.");

            }

            Console.WriteLine($"Decoded JWT identity token {{ name = '{name}' isAdmin = '{isAdmin}' }}");

            return new IdentityFeature(name, isAdmin);
        }
        else
        {
            throw new DispatchException(StatusCode.Unauthorized, "Invalid JWT token.", validationResult.Exception);
        }
    }

    public ReadOnlyMemory<byte> EncodeIdentityToken(string name, bool isAdmin)
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
        return Encoding.UTF8.GetBytes(_tokenHandler.WriteToken(jwtToken));
    }

    internal JwtAuthenticationBearer(string secretKey)
    {
        _tokenHandler = new JwtSecurityTokenHandler();
        var issuerKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        _signingCredentials = new SigningCredentials(issuerKey, SecurityAlgorithms.HmacSha256);

        _tokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            ValidateAudience = true,
            ValidateIssuer = true,
            ValidateLifetime = true,
            RequireSignedTokens = true,
            ValidAudience = "Authorization example",
            ValidIssuer = "icerpc://127.0.0.1",
            IssuerSigningKey = issuerKey,
            ClockSkew = TimeSpan.FromSeconds(5),
        };
    }
}
