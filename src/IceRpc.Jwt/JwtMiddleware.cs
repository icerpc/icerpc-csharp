// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;
using Microsoft.IdentityModel.Tokens;
using System.Buffers;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;

namespace IceRpc.Jwt;

/// <summary>A middleware that decodes the request Jwt field into a Jwt security token and validates it, on the way
/// back it encodes the token from IJwtFeature in the Jwt response field.</summary>
public class JwtMiddleware : IDispatcher
{
    private readonly IDispatcher _next;
    private readonly JwtSecurityTokenHandler _tokenHandler = new();
    private readonly TokenValidationParameters _tokenValidationParameters;

    /// <summary>Constructs a Jwt middleware.</summary>
    /// <param name="next">The next dispatcher in the dispatch pipeline.</param>
    /// <param name="parameters">The Jwt token validation parameters.</param>
    public JwtMiddleware(IDispatcher next, TokenValidationParameters? parameters = null)
    {
        _next = next;
        _tokenValidationParameters = parameters ?? new TokenValidationParameters();
    }

    /// <inheritdoc/>
    public async ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancel)
    {
        JwtSecurityToken? jwtSecurityToken = null;
        // Decode Jwt from Fields and set corresponding feature.
        if (request.Fields.TryGetValue(RequestFieldKey.Jwt, out ReadOnlySequence<byte> value))
        {
            Debug.Assert(request.Protocol == Protocol.IceRpc);
            string token = DecodeToken(value);
            TokenValidationResult result =
                await _tokenHandler.ValidateTokenAsync(token, _tokenValidationParameters).ConfigureAwait(false);
            if (result.IsValid)
            {
                Debug.Assert(result.SecurityToken is JwtSecurityToken);
                jwtSecurityToken = (JwtSecurityToken)result.SecurityToken;
                request.Features = request.Features.With<IJwtFeature>(new JwtFeature(jwtSecurityToken));
            }
            else
            {
                throw new DispatchException(
                    "Invalid JWT token",
                    DispatchErrorCode.InvalidCredentials,
                    result.Exception);
            }
        }

        // Ensure that request features is not read-only to allow dispatch setting the IJwtFeature.
        if (request.Features.IsReadOnly)
        {
            request.Features = new FeatureCollection(request.Features);
        }

        OutgoingResponse response = await _next.DispatchAsync(request, cancel).ConfigureAwait(false);
        
        // If the Jwt token changed we have to encode it in the response fields.
        if (request.Features.Get<IJwtFeature>() is IJwtFeature jwtFeature && jwtFeature.Token != jwtSecurityToken)
        {
            response.Fields = response.Fields.With(
                ResponseFieldKey.Jwt,
                (ref SliceEncoder encoder) => encoder.EncodeString(_tokenHandler.WriteToken(jwtFeature.Token)));
        }
        return response;

        static string DecodeToken(ReadOnlySequence<byte> value)
        {
            var decoder = new SliceDecoder(value, SliceEncoding.Slice2);
            return decoder.DecodeString();
        }
    }
}
