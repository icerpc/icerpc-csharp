// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>Helper class to read and write telemetry data in binary context.</summary>
    internal static class TelemetryContext
    {
        public static void WriteContext(Activity activity, OutgoingRequestFrame request)
        {
            if (activity.IdFormat != ActivityIdFormat.W3C)
            {
                throw new ArgumentException("only W3C ID format is supported with IceRpc", nameof(activity));
            }

            request.BinaryContextOverride.Add(
                (int)BinaryContextKey.TelemetryContext,
                ostr =>
                {
                    // W3C traceparent binary encoding (1 byte version, 16 bytes trace Id, 8 bytes span Id,
                    // 1 byte flags) https://www.w3.org/TR/trace-context/#traceparent-header-field-values
                    ostr.WriteByte(0);
                    Span<byte> buffer = stackalloc byte[16];
                    activity.TraceId.CopyTo(buffer);
                    ostr.WriteByteSpan(buffer);
                    activity.SpanId.CopyTo(buffer);
                    ostr.WriteByteSpan(buffer.Slice(0, 8));
                    ostr.WriteByte(activity.ActivityTraceFlags == ActivityTraceFlags.None ? 0 : 1);

                    // Tracestate encoded as an optional string
                    BitSequence bitSequence = ostr.WriteBitSequence(1);
                    if (activity.TraceStateString != null)
                    {
                        ostr.WriteString(activity.TraceStateString);
                    }
                    else
                    {
                        bitSequence[0] = false;
                    }

                    // Baggage encoded as a sequence<(string, string?)>                    
                    ostr.WriteSequence(activity.Baggage, (ostr, entry) =>
                    {
                        BitSequence bitSequence = ostr.WriteBitSequence(1);
                        ostr.WriteString(entry.Key);
                        if (entry.Value != null)
                        {
                            ostr.WriteString(entry.Value);
                        }
                        else
                        {
                            bitSequence[0] = false;
                        }
                    });
                });
        }
    }
}
