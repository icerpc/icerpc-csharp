// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc
{
    /// <summary>An interceptor that starts an <see cref="Activity"/> per request, following OpenTelemetry
    /// conventions. The Activity is started if <see cref="Activity.Current"/> is not null or if "IceRpc" logging is
    /// enabled. The activity context is written in the request fields and can be restored by installing a
    /// <see cref="TelemetryMiddleware"/>.</summary>
    public class TelemetryInterceptor : IInvoker
    {
        private readonly IInvoker _next;
        private readonly ILogger _logger;
        private readonly Configure.TelemetryOptions _options;

        /// <summary>Constructs a telemetry interceptor.</summary>
        /// <param name="next">The next invoker in the invocation pipeline.</param>
        /// <param name="options">The options to configure the telemetry interceptor.</param>
        public TelemetryInterceptor(IInvoker next, Configure.TelemetryOptions options)
        {
            _next = next;
            _options = options;
            _logger = options.LoggerFactory?.CreateLogger("IceRpc") ?? NullLogger.Instance;
        }

        async Task<IncomingResponse> IInvoker.InvokeAsync(OutgoingRequest request, CancellationToken cancel)
        {
            if (request.Protocol == Protocol.Ice2)
            {
                Activity? activity = _options.ActivitySource?.CreateActivity(
                    $"{request.Path}/{request.Operation}",
                    ActivityKind.Client);
                if (activity == null && (_logger.IsEnabled(LogLevel.Critical) || Activity.Current != null))
                {
                    activity = new Activity($"{request.Path}/{request.Operation}");
                }

                if (activity != null)
                {
                    activity.AddTag("rpc.system", "icerpc");
                    activity.AddTag("rpc.service", request.Path);
                    activity.AddTag("rpc.method", request.Operation);
                    // TODO add additional attributes
                    // https://github.com/open-telemetry/opentelemetry-specification/blob/main/specification/trace/semantic_conventions/rpc.md#common-remote-procedure-call-conventions
                    activity.Start();
                }

                WriteActivityContext(request);

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

        private static void WriteActivityContext(OutgoingRequest request)
        {
            Debug.Assert(request.Protocol == Protocol.Ice2);
            if (Activity.Current is Activity activity && activity.Id != null)
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
                //    byte version;
                //    // ActivityTraceId 16 bytes
                //    ulong activityTraceId0;
                //    ulong activityTraceId1;
                //    // ActivitySpanId 8 bytes
                //    ulong activitySpanId
                //    // ActivityTraceFlags 1 byte
                //    byte ActivityTraceFlags;
                //    string traceStateString;
                //    Baggage baggage;
                // }

                request.Fields.Add(
                    (int)Ice2FieldKey.TraceContext,
                    encoder =>
                    {
                        // W3C traceparent binary encoding (1 byte version, 16 bytes trace Id, 8 bytes span Id,
                        // 1 byte flags) https://www.w3.org/TR/trace-context/#traceparent-header-field-values
                        encoder.EncodeByte(0);
                        Span<byte> buffer = stackalloc byte[16];
                        activity.TraceId.CopyTo(buffer);
                        encoder.WriteByteSpan(buffer);
                        activity.SpanId.CopyTo(buffer[0..8]);
                        encoder.WriteByteSpan(buffer[0..8]);
                        encoder.EncodeByte((byte)activity.ActivityTraceFlags);

                        // Tracestate encoded as an string
                        encoder.EncodeString(activity.TraceStateString ?? "");

                        // Baggage encoded as a sequence<BaggageEntry>
                        encoder.EncodeSequence(activity.Baggage, (encoder, entry) =>
                        {
                            encoder.EncodeString(entry.Key);
                            encoder.EncodeString(entry.Value ?? "");
                        });
                    });
            }
        }
    }
}
