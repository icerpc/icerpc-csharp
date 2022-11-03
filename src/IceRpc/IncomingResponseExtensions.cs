// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc;

/// <summary>Provides an extension method to decode dispatch exceptions.</summary>
public static class IncomingResponseExtensions
{
    /// <summary>Decodes a response with a <see cref="ResultType.Failure" /> result type.</summary>
    /// <param name="response">The incoming response.</param>
    /// <param name="request">The outgoing request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The decoded <see cref="DispatchException" />.</returns>
    public static ValueTask<DispatchException> DecodeDispatchExceptionAsync(
        this IncomingResponse response,
        OutgoingRequest request,
        CancellationToken cancellationToken = default) =>
        response.Protocol.DecodeDispatchExceptionAsync(response, request, cancellationToken);
}
