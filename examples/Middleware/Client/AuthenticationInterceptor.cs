// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using IceRpc.Slice;

public class AuthenticationInterceptor : IInvoker
{
    private readonly IInvoker _next;

    /// <summary>Constructs a AuthenticationInterceptor interceptor.</summary>
    /// <param name="next">The next dispatcher in the dispatch pipeline.</param>
    public AuthenticationInterceptor(IInvoker next)
    {
        _next = next;
    }

    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel = default)
    {
        // Retrieve the feature collection containing the password
        Dictionary<string, string>? headers = request.Features.Get<Dictionary<string, string>>();
        if (headers != null && headers.TryGetValue("token", out string? token))
        {
            // Set the password in the request fields
            ulong authorizationKey = 100;
            request.Fields = request.Fields.With(
                (RequestFieldKey)authorizationKey,
                (ref SliceEncoder encoder) => encoder.EncodeString(token));
        }

        return _next.InvokeAsync(request, cancel);
    }
}
