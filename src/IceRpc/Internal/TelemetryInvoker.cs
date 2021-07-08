// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Internal
{
    /// <summary>The implementation of <see cref="Interceptors.CustomTelemetry(Interceptors.TelemetryOptions)"/>.</summary>
    internal sealed class TelemetryInvoker : IInvoker
    {
        private readonly ILogger _logger;
        private readonly IInvoker _next;
        private readonly Interceptors.TelemetryOptions _options;

        /// <inheritdoc/>
        public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
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

        internal TelemetryInvoker(Interceptors.TelemetryOptions options, IInvoker next)
        {
            _options = options;
            _logger = (options.LoggerFactory ?? NullLoggerFactory.Instance).CreateLogger("IceRpc");
            _next = next;
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
                    writer =>
                    {
                        // W3C traceparent binary encoding (1 byte version, 16 bytes trace Id, 8 bytes span Id,
                        // 1 byte flags) https://www.w3.org/TR/trace-context/#traceparent-header-field-values
                        writer.WriteByte(0);
                        Span<byte> buffer = stackalloc byte[16];
                        activity.TraceId.CopyTo(buffer);
                        writer.WriteByteSpan(buffer);
                        activity.SpanId.CopyTo(buffer[0..8]);
                        writer.WriteByteSpan(buffer[0..8]);
                        writer.WriteByte((byte)activity.ActivityTraceFlags);

                        // Tracestate encoded as an string
                        writer.WriteString(activity.TraceStateString ?? "");

                        // Baggage encoded as a sequence<BaggageEntry>
                        writer.WriteSequence(activity.Baggage, (writer, entry) =>
                        {
                            writer.WriteString(entry.Key);
                            writer.WriteString(entry.Value ?? "");
                        });
                    });
            }
        }
    }
}
