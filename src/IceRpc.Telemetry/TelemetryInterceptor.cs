// Copyright (c) ZeroC, Inc.

using IceRpc.Extensions.DependencyInjection;
using IceRpc.Slice;
using System.Buffers;
using System.Diagnostics;

namespace IceRpc.Telemetry;

/// <summary>An interceptor that starts an <see cref="Activity" /> per request, following
/// <see href="https://opentelemetry.io/">OpenTelemetry</see> conventions. The activity context is written in the
/// request <see cref="RequestFieldKey.TraceContext" /> field and can be restored on the server-side by installing the
/// <see cref="TelemetryMiddleware" />.</summary>
/// <remarks>The activities are only created for requests using the icerpc protocol.</remarks>
/// <seealso cref="TelemetryPipelineExtensions"/>
/// <seealso cref="TelemetryDispatcherBuilderExtensions"/>
public class TelemetryInterceptor : IInvoker
{
    private readonly IInvoker _next;
    private readonly ActivitySource _activitySource;

    /// <summary>Constructs a telemetry interceptor.</summary>
    /// <param name="next">The next invoker in the invocation pipeline.</param>
    /// <param name="activitySource">The <see cref="ActivitySource" /> used to start the request activity.</param>
    public TelemetryInterceptor(IInvoker next, ActivitySource activitySource)
    {
        _next = next;
        _activitySource = activitySource;
    }

    /// <inheritdoc/>
    public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken)
    {
        if (request.Protocol.HasFields)
        {
            string name = $"{request.ServiceAddress.Path}/{request.Operation}";
            using Activity activity = _activitySource?.CreateActivity(name, ActivityKind.Client) ?? new Activity(name);
            activity.AddTag("rpc.system", "icerpc");
            activity.AddTag("rpc.service", request.ServiceAddress.Path);
            activity.AddTag("rpc.method", request.Operation);
            activity.Start();
            request.Fields = request.Fields.With(
                RequestFieldKey.TraceContext,
                (ref SliceEncoder encoder) => WriteActivityContext(ref encoder, activity));
            return await _next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            return await _next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }

    internal static void WriteActivityContext(ref SliceEncoder encoder, Activity activity)
    {
        if (activity.IdFormat != ActivityIdFormat.W3C)
        {
            throw new NotSupportedException(
                $"The activity ID format '{activity.IdFormat}' is not supported, the only supported activity ID format is 'W3C'.");
        }

        if (activity.Id is null)
        {
            throw new ArgumentException("The activity ID property cannot be null.", nameof(activity.Id));
        }

        // The activity context is written to the field value, as if it has the following Slice definition
        //
        // struct BaggageEntry
        // {
        //    string key;
        //    string value;
        // }
        // sequence<BaggageEntry> Baggage;
        //
        // struct ActivityContext
        // {
        //    // ActivityID version 1 byte
        //    uint8 version;
        //    // ActivityTraceId 16 bytes
        //    uint64 activityTraceId0;
        //    uint64 activityTraceId1;
        //    // ActivitySpanId 8 bytes
        //    uint64 activitySpanId
        //    // ActivityTraceFlags 1 byte
        //    uint8 ActivityTraceFlags;
        //    string traceStateString;
        //    Baggage baggage;
        // }

        // W3C traceparent binary encoding (1 byte version, 16 bytes trace-ID, 8 bytes span-ID,
        // 1 byte flags) https://www.w3.org/TR/trace-context/#traceparent-header-field-values
        encoder.EncodeUInt8(0);

        // Unfortunately we can't use stackalloc.
        using IMemoryOwner<byte> memoryOwner = MemoryPool<byte>.Shared.Rent(16);
        Span<byte> buffer = memoryOwner.Memory.Span[0..16];
        activity.TraceId.CopyTo(buffer);
        encoder.WriteByteSpan(buffer);
        activity.SpanId.CopyTo(buffer[0..8]);
        encoder.WriteByteSpan(buffer[0..8]);
        encoder.EncodeUInt8((byte)activity.ActivityTraceFlags);

        // TraceState encoded as an string
        encoder.EncodeString(activity.TraceStateString ?? "");

        // Baggage encoded as a sequence<BaggageEntry>
        encoder.EncodeSequence(
            activity.Baggage,
            (ref SliceEncoder encoder, KeyValuePair<string, string?> entry) =>
            {
                encoder.EncodeString(entry.Key);
                encoder.EncodeString(entry.Value ?? "");
            });
    }
}
