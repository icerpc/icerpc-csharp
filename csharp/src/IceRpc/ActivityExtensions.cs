// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Headers;
using System.Web;

namespace IceRpc
{
    /// <summary>Helper class to read and write the Activity context from and to IceRpc binary context.</summary>
    internal static class ActivityExtensions
    {
        internal static void WriteActivityContext(this Activity activity, OutgoingRequestFrame request)
        {
            if (activity.IdFormat != ActivityIdFormat.W3C)
            {
                throw new ArgumentException("only W3C ID format is supported with IceRpc", nameof(activity));
            }

            if (activity.Id == null)
            {
                throw new ArgumentException("invalid null activity ID", nameof(activity));
            }

            if (request.Protocol == Protocol.Ice1)
            {
                request.WritableContext["traceparent"] = activity.Id;
                if (activity.TraceStateString != null)
                {
                    request.WritableContext["tracestate"] = activity.TraceStateString;
                }

                using IEnumerator<KeyValuePair<string, string?>> e = activity.Baggage.GetEnumerator();
                if (e.MoveNext())
                {
                    var baggage = new List<string>();
                    do
                    {
                        baggage.Add(new NameValueHeaderValue(HttpUtility.UrlEncode(e.Current.Key),
                                                                HttpUtility.UrlEncode(e.Current.Value)).ToString());
                    }
                    while (e.MoveNext());
                    request.WritableContext["baggage"] = string.Join(',', baggage);
                }
            }
            else
            {
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
                        activity.SpanId.CopyTo(buffer[0..8]);
                        ostr.WriteByteSpan(buffer[0..8]);
                        ostr.WriteByte((byte)activity.ActivityTraceFlags);

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

        internal static void RestoreActivityContext(this Activity activity, IncomingRequestFrame request)
        {
            if (request.Protocol == Protocol.Ice1)
            {
                if (request.Context.TryGetValue("traceparent", out string? parentId))
                {
                    activity.SetParentId(parentId);
                    if (request.Context.TryGetValue("tracestate", out string? traceState))
                    {
                        activity.TraceStateString = traceState;
                    }

                    if (request.Context.TryGetValue("baggage", out string? baggage))
                    {
                        string[] baggageItems = baggage.Split(new char[] { ',' },
                            StringSplitOptions.RemoveEmptyEntries);
                        for (int i = baggageItems.Length - 1; i >= 0; i--)
                        {
                            if (NameValueHeaderValue.TryParse(baggageItems[i], out NameValueHeaderValue? baggageItem))
                            {
                                if (baggageItem.Value?.Length > 0)
                                {
                                    activity.AddBaggage(baggageItem.Name.ToString(),
                                                        HttpUtility.UrlDecode(baggageItem.Value));
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                if (request.BinaryContext.TryGetValue((int)BinaryContextKey.TelemetryContext,
                                                      out ReadOnlyMemory<byte> buffer))
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

                    // Read tracestate encoded as an optional string
                    var istr = new InputStream(buffer[i..], Encoding.V20);
                    ReadOnlyBitSequence bitSequence = istr.ReadBitSequence(1);
                    if (bitSequence[0])
                    {
                        activity.TraceStateString = istr.ReadString();
                    }

                    // The min element size is 2 bytes empty string is 1 byte, and null value string
                    // 1 byte for the bit sequence.
                    IEnumerable<(string key, string? value)> baggage = istr.ReadSequence(
                        minElementSize: 2,
                        istr =>
                        {
                            ReadOnlyBitSequence bitSequence = istr.ReadBitSequence(1);
                            string key = istr.ReadString();
                            string? value = null;
                            if (bitSequence[0])
                            {
                                value = istr.ReadString();
                            }
                            return (key, value);
                        });

                    // Restore in reverse order to keep the order in witch the peer add baggage entries,
                    // this is important when there are duplicate keys.
                    foreach ((string key, string? value) in baggage.Reverse())
                    {
                        activity.AddBaggage(key, value);
                    }
                }
            }
        }
    }
}
