// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using System.Buffers;
using System.Diagnostics;

namespace IceRpc.Telemetry;

/// <summary>A middleware that starts an <see cref="Activity"/> per request, following OpenTelemetry
/// conventions. The Activity is started if <see cref="Activity.Current"/> is not null or if "IceRpc" logging
/// is enabled. The middleware restores the parent invocation activity before starting the dispatch activity.</summary>
/// <seealso cref="TelemetryInterceptor"/>
public class TelemetryMiddleware : IDispatcher
{
    private readonly IDispatcher _next;
    private readonly ActivitySource _activitySource;

    /// <summary>Constructs a telemetry middleware.</summary>
    /// <param name="next">The next dispatcher in the dispatch pipeline.</param>
    /// <param name="activitySource">The <see cref="ActivitySource"/> is used to start the request activity.</param>
    public TelemetryMiddleware(IDispatcher next, ActivitySource activitySource)
    {
        _next = next;
        _activitySource = activitySource;
    }

    /// <inheritdoc/>
    public async ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken)
    {
        if (request.Protocol.HasFields)
        {
            string name = $"{request.Path}/{request.Operation}";
            using Activity activity = _activitySource.CreateActivity(name, ActivityKind.Server) ?? new Activity(name);

            activity.AddTag("rpc.system", "icerpc");
            activity.AddTag("rpc.service", request.Path);
            activity.AddTag("rpc.method", request.Operation);
            // TODO add additional attributes
            // https://github.com/open-telemetry/opentelemetry-specification/blob/main/specification/trace/semantic_conventions/rpc.md#common-remote-procedure-call-conventions
            if (request.Fields.TryGetValue(RequestFieldKey.TraceContext, out ReadOnlySequence<byte> buffer))
            {
                RestoreActivityContext(buffer, activity);
            }
            activity.Start();
            return await _next.DispatchAsync(request, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            return await _next.DispatchAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }

    internal static void RestoreActivityContext(ReadOnlySequence<byte> buffer, Activity activity)
    {
        var decoder = new SliceDecoder(buffer, SliceEncoding.Slice2);

        // Read W3C traceparent binary encoding (1 byte version, 16 bytes trace-ID, 8 bytes span-ID,
        // 1 byte flags) https://www.w3.org/TR/trace-context/#traceparent-header-field-values

        byte traceIdVersion = decoder.DecodeUInt8();

        using IMemoryOwner<byte> memoryOwner = MemoryPool<byte>.Shared.Rent(16);
        Span<byte> traceIdSpan = memoryOwner.Memory.Span[0..16];
        decoder.CopyTo(traceIdSpan);
        var traceId = ActivityTraceId.CreateFromBytes(traceIdSpan);

        Span<byte> spanIdSpan = memoryOwner.Memory.Span[0..8];
        decoder.CopyTo(spanIdSpan);
        var spanId = ActivitySpanId.CreateFromBytes(spanIdSpan);

        var traceFlags = (ActivityTraceFlags)decoder.DecodeUInt8();

        activity.SetParentId(traceId, spanId, traceFlags);

        // Read tracestate encoded as a string
        activity.TraceStateString = decoder.DecodeString();

        IEnumerable<(string Key, string Value)> baggage = decoder.DecodeSequence(
            (ref SliceDecoder decoder) =>
            {
                string key = decoder.DecodeString();
                string value = decoder.DecodeString();
                return (key, value);
            });

        // Restore in reverse order to keep the order in witch the peer add baggage entries,
        // this is important when there are duplicate keys.
        foreach ((string key, string value) in baggage.Reverse())
        {
            activity.AddBaggage(key, value);
        }
    }
}
