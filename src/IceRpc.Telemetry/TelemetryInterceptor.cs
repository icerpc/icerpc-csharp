// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Buffers;
using System.Diagnostics;

namespace IceRpc.Telemetry;

/// <summary>An interceptor that starts an <see cref="Activity"/> per request, following OpenTelemetry
/// conventions. The Activity is started if <see cref="Activity.Current"/> is not null or if "IceRpc" logging is
/// enabled. The activity context is written in the request fields and can be restored by installing a
/// <see cref="TelemetryMiddleware"/>.</summary>
public class TelemetryInterceptor : IInvoker
{
    private readonly IInvoker _next;
    private readonly ILogger _logger;
    private readonly ActivitySource? _activitySource;

    /// <summary>Constructs a telemetry interceptor.</summary>
    /// <param name="next">The next invoker in the invocation pipeline.</param>
    /// <param name="activitySource">If set to a non null object the <see cref="ActivitySource"/> is used to start the
    /// request and response activities.</param>
    /// <param name="loggerFactory">The logger factory used to create the IceRpc logger.</param>
    public TelemetryInterceptor(
        IInvoker next,
        ActivitySource? activitySource = null,
        ILoggerFactory? loggerFactory = null)
    {
        _next = next;
        _activitySource = activitySource;
        _logger = loggerFactory?.CreateLogger("IceRpc") ?? NullLogger.Instance;
    }

    /// <inheritdoc/>
    public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
    {
        if (request.Protocol.HasFields)
        {
            Activity? activity = _activitySource?.CreateActivity(
                $"{request.Proxy.Path}/{request.Operation}",
                ActivityKind.Client);
            if (activity == null && (_logger.IsEnabled(LogLevel.Critical) || Activity.Current != null))
            {
                activity = new Activity($"{request.Proxy.Path}/{request.Operation}");
            }

            if (activity != null)
            {
                activity.AddTag("rpc.system", "icerpc");
                activity.AddTag("rpc.service", request.Proxy.Path);
                activity.AddTag("rpc.method", request.Operation);
                // TODO add additional attributes
                // https://github.com/open-telemetry/opentelemetry-specification/blob/main/specification/trace/semantic_conventions/rpc.md#common-remote-procedure-call-conventions
                activity.Start();

                request.Fields = request.Fields.With(RequestFieldKey.TraceContext,
                    (ref SliceEncoder encoder) => WriteActivityContext(ref encoder, activity));
            }

            try
            {
                return await _next.InvokeAsync(request, cancel).ConfigureAwait(false);
            }
            finally
            {
                activity?.Stop();
                activity?.Dispose();
            }
        }
        else
        {
            return await _next.InvokeAsync(request, cancel).ConfigureAwait(false);
        }
    }

    internal static void WriteActivityContext(ref SliceEncoder encoder, Activity activity)
    {
        if (activity.IdFormat != ActivityIdFormat.W3C)
        {
            throw new ArgumentException("only W3C ID format is supported with IceRpc", nameof(activity));
        }

        if (activity.Id == null)
        {
            throw new ArgumentException("invalid null activity ID", nameof(activity));
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

        // Tracestate encoded as an string
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
