// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;
using System.Buffers;

namespace IceRpc.RequestContext;

/// <summary>Represents a middleware that decodes the request context field into a request context feature.</summary>
/// <remarks>Both the ice protocol and the icerpc protocol can transmit the request context field with requests; while
/// icerpc can transmit all request fields, ice can only transmit this request context field and the idempotent field.
/// </remarks>
public class RequestContextMiddleware : IDispatcher
{
    private readonly IDispatcher _next;

    /// <summary>Constructs a request context middleware.</summary>
    /// <param name="next">The next dispatcher in the dispatch pipeline.</param>
    public RequestContextMiddleware(IDispatcher next) => _next = next;

    /// <inheritdoc/>
    public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken)
    {
        // Decode Context from Fields and set corresponding feature.
        if (request.Fields.TryGetValue(RequestFieldKey.Context, out ReadOnlySequence<byte> value))
        {
            var decoder = new SliceDecoder(
                value,
                request.Protocol == Protocol.Ice ? SliceEncoding.Slice1 : SliceEncoding.Slice2);

            Dictionary<string, string> context = decoder.DecodeDictionary(
                size => new Dictionary<string, string>(size),
                keyDecodeFunc: (ref SliceDecoder decoder) => decoder.DecodeString(),
                valueDecodeFunc: (ref SliceDecoder decoder) => decoder.DecodeString());
            if (context.Count > 0)
            {
                request.Features = request.Features.With<IRequestContextFeature>(new RequestContextFeature(context));
            }
        }
        return _next.DispatchAsync(request, cancellationToken);
    }
}
