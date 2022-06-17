// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;
using Microsoft.IdentityModel.Tokens;
using System.Buffers;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;

namespace IceRpc.Jwt;

/// <summary>A middleware that decodes the request Jwt field into a Jwt security token and validates it, on the way
/// back it encode the token from IJwtFeature in the Jwt response field.</summary>
/// <exception cref="ArgumentException">xx.</exception>
public class JwtMiddleware : IDispatcher
{
    private readonly IDispatcher _next;
    private readonly JwtSecurityTokenHandler _tokenHandler = new();
    private readonly TokenValidationParameters _validationParameteres;

    /// <summary>Constructs a Jwt middleware.</summary>
    /// <param name="next">The next dispatcher in the dispatch pipeline.</param>
    public JwtMiddleware(IDispatcher next)
        : this(new TokenValidationParameters(), next)
    {
    }

    /// <summary>Constructs a Jwt middleware.</summary>
    /// <param name="validationParameteres">The Jwt token validation parameters.</param>
    /// <param name="next">The next dispatcher in the dispatch pipeline.</param>
    public JwtMiddleware(TokenValidationParameters validationParameteres, IDispatcher next)
    {
        _validationParameteres = validationParameteres;
        _next = next;
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
                await _tokenHandler.ValidateTokenAsync(token, _validationParameteres).ConfigureAwait(false);
            if (result.IsValid)
            {
                Debug.Assert(result.SecurityToken is JwtSecurityToken);
                jwtSecurityToken = (JwtSecurityToken)result.SecurityToken;
                request.Features = request.Features.With<IJwtFeature>(new JwtFeature(jwtSecurityToken));
            }
            else
            {
                throw new DispatchException(
                    "Invalid Jwt token",
                    DispatchErrorCode.UnhandledException,
                    result.Exception);
            }
        }

        // Ensure that request features is not read-only to allow dispatch setting the IJwtFeature
        if (request.Features.IsReadOnly)
        {
            request.Features = new FeatureCollection(request.Features);
        }

        OutgoingResponse response = await _next.DispatchAsync(request, cancel).ConfigureAwait(false);
        // If the Jwt token changed we  have to encode it in the response features
        if (request.Features.Get<IJwtFeature>() is IJwtFeature jwtFeature && jwtFeature.Value != jwtSecurityToken)
        {
            response.Fields = response.Fields.With(
                ResponseFieldKey.Jwt,
                (ref SliceEncoder encoder) => encoder.EncodeString(_tokenHandler.WriteToken(jwtFeature.Value)));
        }
        return response;

        static string DecodeToken(ReadOnlySequence<byte> value)
        {
            var decoder = new SliceDecoder(value, SliceEncoding.Slice2);
            return decoder.DecodeString();
        }
    }
}
