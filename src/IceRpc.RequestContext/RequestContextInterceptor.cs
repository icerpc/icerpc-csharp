// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;

namespace IceRpc.RequestContext;

/// <summary>An interceptor that encodes the request context into a request field.</summary>
public class RequestContextInterceptor : IInvoker
{
    private readonly IInvoker _next;

    /// <summary>Constructs a request context interceptor.</summary>
    /// <param name="next">The next invoker in the invocation pipeline.</param>
    public RequestContextInterceptor(IInvoker next) => _next = next;

    /// <inheritdoc/>
    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken)
    {
        if (request.Features.Get<IRequestContextFeature>()?.Value is IDictionary<string, string> context)
        {
            if (context.Count == 0)
            {
                // make sure it's not set.
                request.Fields = request.Fields.Without(RequestFieldKey.Context);
            }
            else
            {
                request.Fields = request.Fields.With(
                    RequestFieldKey.Context,
                    (ref SliceEncoder encoder) => encoder.EncodeDictionary(
                        context,
                        (ref SliceEncoder encoder, string value) => encoder.EncodeString(value),
                        (ref SliceEncoder encoder, string value) => encoder.EncodeString(value)));
            }
        }
        return _next.InvokeAsync(request, cancellationToken);
    }
}
