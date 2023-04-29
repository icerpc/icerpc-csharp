// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;

namespace IceRpc.RequestContext;

/// <summary>Represents an interceptor that encodes the request context feature into a request context field.</summary>
/// <remarks>Both the ice protocol and the icerpc protocol can transmit the request context field with requests; while
/// icerpc can transmit all request fields, ice can only transmit this request context field and the idempotent field.
/// </remarks>
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
