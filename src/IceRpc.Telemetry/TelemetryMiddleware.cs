// Copyright (c) ZeroC, Inc.

using IceRpc.Extensions.DependencyInjection;
using System.Buffers;
using System.Diagnostics;
using ZeroC.Slice.Codec;

namespace IceRpc.Telemetry;

/// <summary>A middleware that starts an <see cref="Activity" /> per request, following
/// <see href="https://opentelemetry.io/">OpenTelemetry</see> conventions. The middleware restores the parent invocation
/// activity from the request <see cref="RequestFieldKey.TraceContext" /> field before starting the dispatch activity.
/// </summary>
/// <remarks>The activities are only created for requests using the icerpc protocol.</remarks>
/// <seealso cref="TelemetryRouterExtensions" />
/// <seealso cref="TelemetryDispatcherBuilderExtensions"/>
public class TelemetryMiddleware : IDispatcher
{
    private readonly IDispatcher _next;
    private readonly ActivitySource _activitySource;

    /// <summary>Constructs a telemetry middleware.</summary>
    /// <param name="next">The next dispatcher in the dispatch pipeline.</param>
    /// <param name="activitySource">The <see cref="ActivitySource" /> is used to start the request activity.</param>
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
        var decoder = new SliceDecoder(buffer);

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

        // Read TraceState encoded as a string
        activity.TraceStateString = decoder.DecodeString();

        // Decode the baggage sequence with an explicit entry-count cap. Well-behaved peers clip their
        // outgoing baggage at TelemetryInterceptor.MaxBaggageEntries; exceeding the cap here indicates
        // a malformed or malicious sender. Check the count before decoding the entries so we don't
        // allocate or deserialize anything for a bogus sequence.
        int count = decoder.DecodeSize();
        if (count > TelemetryInterceptor.MaxBaggageEntries)
        {
            throw new InvalidDataException(
                $"The baggage sequence has {count} entries, which exceeds the maximum of {TelemetryInterceptor.MaxBaggageEntries}.");
        }

        var baggage = new (string Key, string Value)[count];
        for (int i = 0; i < count; i++)
        {
            string key = decoder.DecodeString();
            string value = decoder.DecodeString();
            baggage[i] = (key, value);
        }

        // Restore in reverse order to keep the order in witch the peer add baggage entries,
        // this is important when there are duplicate keys.
        for (int i = count - 1; i >= 0; i--)
        {
            activity.AddBaggage(baggage[i].Key, baggage[i].Value);
        }
    }
}
