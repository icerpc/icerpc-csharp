// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using IceRpc.Slice;

public class AuthenticationMiddleware : IDispatcher
{
    private readonly IDispatcher _next;
    private readonly string _password;

    /// <summary>Constructs a AuthenticationMiddleware middleware.</summary>
    /// <param name="next">The next dispatcher in the dispatch pipeline.</param>
    public AuthenticationMiddleware(
        IDispatcher next, string password)
    {
        _next = next;
        _password = password;
    }

    public ValueTask<OutgoingResponse> DispatchAsync(
        IncomingRequest request,
        CancellationToken cancel = default)
    {
        // Retrieve the token field from the request
        string? token = request.Fields.DecodeValue(
            (RequestFieldKey)100,
            (ref SliceDecoder decoder) => decoder.DecodeString());

        // Set a feature containing the token and password
        var authorization = new Dictionary<string, string?>();
        authorization.Add("token", token);
        authorization.Add("authorization", _password);
        request.Features.Set<Dictionary<string, string?>>(authorization);

        // Allow the request to continue
        return _next.DispatchAsync(request, cancel);
    }
}
