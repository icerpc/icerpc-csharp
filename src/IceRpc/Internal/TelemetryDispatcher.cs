// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Internal
{
    /// <summary>The implementation of <see cref="Middleware.CustomTelemetry(Middleware.TelemetryOptions)"/>.</summary>
    internal sealed class TelemetryDispatcher : IDispatcher
    {
        private readonly ILogger _logger;
        private readonly IDispatcher _next;
        private readonly Middleware.TelemetryOptions _options;

        /// <inheritdoc/>
        public async ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancel)
        {
            if (request.Protocol != Protocol.Ice1)
            {
                Activity? activity = _options.ActivitySource?.CreateActivity(
                    $"{request.Path}/{request.Operation}",
                    ActivityKind.Server);
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
                    RestoreActivityContext(request, activity);
                    activity.Start();
                }

                try
                {
                    return await _next.DispatchAsync(request, cancel).ConfigureAwait(false);
                }
                finally
                {
                    activity?.Stop();
                    activity?.Dispose();
                }
            }
            else
            {
                return await _next.DispatchAsync(request, cancel).ConfigureAwait(false);
            }
        }

        internal TelemetryDispatcher(Middleware.TelemetryOptions options, IDispatcher next)
        {
            _options = options;
            _logger = (options.LoggerFactory ?? NullLoggerFactory.Instance).CreateLogger("IceRpc");
            _next = next;
        }

        private static void RestoreActivityContext(IncomingRequest request, Activity activity)
        {
            Debug.Assert(request.Protocol != Protocol.Ice1);
            if (request.Fields.TryGetValue((int)Ice2FieldKey.TraceContext, out ReadOnlyMemory<byte> buffer))
            {
                // Read W3C traceparent binary encoding (1 byte version, 16 bytes trace Id, 8 bytes span Id,
                // 1 byte flags) https://www.w3.org/TR/trace-context/#traceparent-header-field-values
                int i = 0;
                byte traceIdVersion = buffer.Span[i++];
                var traceId = ActivityTraceId.CreateFromBytes(buffer.Span.Slice(i, 16));
                i += 16;
                var spanId = ActivitySpanId.CreateFromBytes(buffer.Span.Slice(i, 8));
                i += 8;
                var traceFlags = (ActivityTraceFlags)buffer.Span[i++];

                activity.SetParentId(traceId, spanId, traceFlags);

                // Read tracestate encoded as a string
                var decoder = new IceDecoder(buffer[i..], Encoding.Ice20);
                activity.TraceStateString = decoder.DecodeString();

                // The min element size is 2 bytes for a struct with two empty strings.
                IEnumerable<(string key, string value)> baggage = decoder.DecodeSequence(
                    minElementSize: 2,
                    decoder =>
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
    }
}
