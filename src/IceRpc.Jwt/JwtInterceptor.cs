// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using System.Buffers;

namespace IceRpc.Jwt;

/// <summary>An interceptor that encodes a Jwt security token into a request field.</summary>
public class JwtInterceptor : IInvoker
{
    private readonly IInvoker _next;
    private string? _token;

    /// <summary>Constructs a request Jwt interceptor.</summary>
    /// <param name="next">The next invoker in the invocation pipeline.</param>
    public JwtInterceptor(IInvoker next) => _next = next;

    /// <inheritdoc/>
    public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
    {
        // If we have a Jwt security token encode it in the Jwt request field.
        if (_token != null)
        {
            request.Fields = request.Fields.With(
                RequestFieldKey.Jwt,
                (ref SliceEncoder encoder) => encoder.EncodeString(_token));
        }
        IncomingResponse response = await _next.InvokeAsync(request, cancel).ConfigureAwait(false);

        // Check if there is a Jwt field and decode the token.
        if (response.Fields.TryGetValue(ResponseFieldKey.Jwt, out ReadOnlySequence<byte> value))
        {
            _token = DecodeToken(value);
        }
        return response;

        static string DecodeToken(ReadOnlySequence<byte> value)
        {
            var decoder = new SliceDecoder(value, SliceEncoding.Slice2);
            return decoder.DecodeString();
        }
    }
}
