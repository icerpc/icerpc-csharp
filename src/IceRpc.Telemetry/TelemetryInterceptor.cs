// Copyright (c) ZeroC, Inc.

using IceRpc.Extensions.DependencyInjection;
using IceRpc.Telemetry.Internal;
using System.Diagnostics;
using ZeroC.Slice.Codec;

namespace IceRpc.Telemetry;

/// <summary>An interceptor that starts an <see cref="Activity" /> per request, following
/// <see href="https://opentelemetry.io/">OpenTelemetry</see> conventions. The activity context is written in the
/// request <see cref="RequestFieldKey.TraceContext" /> field and can be restored on the server-side by installing the
/// <see cref="TelemetryMiddleware" />.</summary>
/// <remarks>The interceptor creates an activity only for a request that uses the icerpc protocol. The name of this
/// activity is <c>{path}/{operation}</c>, and its tags are:
/// <list type="bullet">
/// <item><description><c>rpc.system.name</c>: <c>icerpc</c>.</description></item>
/// <item><description><c>rpc.method</c>: the operation name.</description></item>
/// <item><description><c>icerpc.path</c>: the path of the service address.</description></item>
/// <item><description><c>rpc.status_code</c>: the status code carried by the response.</description></item>
/// <item><description><c>error.type</c>: a status code other than <see cref="StatusCode.Ok" />, or the full name of
/// the exception thrown by the invocation.</description></item>
/// <item><description><c>network.peer.address</c> and <c>network.peer.port</c>: the IP address and the port of the
/// server.</description></item>
/// <item><description><c>icerpc.canceled</c>: <see langword="true" /> when the invocation was canceled.
/// </description></item>
/// </list>
/// An activity with an <c>error.type</c> tag has the <see cref="ActivityStatusCode.Error" /> status; the status of any
/// other activity is unset.</remarks>
/// <seealso cref="TelemetryPipelineExtensions"/>
/// <seealso cref="TelemetryDispatcherBuilderExtensions"/>
public class TelemetryInterceptor : IInvoker
{
    // The W3C Baggage spec guarantees propagation of all entries only when the baggage has at most 64
    // list-members and fits in 8192 bytes. We clip at 64 entries — the spec-mandated floor — so a
    // strictly spec-conforming peer in any language can always round-trip the baggage we send. Clipping
    // here also prevents amplification of entry count across forwarded hops.
    internal const int MaxBaggageEntries = 64;

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
            using Activity activity = _activitySource.CreateRequestActivity(
                ActivityKind.Client,
                request.ServiceAddress.Path,
                request.Operation);
            activity.SetIdFormat(ActivityIdFormat.W3C);
            activity.Start();
            request.Fields = request.Fields.With(RequestFieldKey.TraceContext, activity, WriteActivityContext);
            try
            {
                IncomingResponse response = await _next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
                activity.SetNetworkPeer(response.ConnectionContext);
                activity.SetTag("rpc.status_code", response.StatusCode.ToString());
                if (response.StatusCode != StatusCode.Ok)
                {
                    activity.SetTag("error.type", response.StatusCode.ToErrorType());
                    activity.SetStatus(ActivityStatusCode.Error, response.ErrorMessage);
                }
                return response;
            }
            catch (OperationCanceledException exception) when (
                cancellationToken.IsCancellationRequested && exception.CancellationToken == cancellationToken)
            {
                activity.SetTag("icerpc.canceled", true);
                throw;
            }
            catch (Exception exception)
            {
                activity.SetTag("error.type", exception.GetType().FullName);
                activity.SetStatus(ActivityStatusCode.Error, exception.Message);
                throw;
            }
        }
        else
        {
            return await _next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }

    internal static void WriteActivityContext(ref SliceEncoder encoder, Activity activity)
    {
        Debug.Assert(activity.IdFormat == ActivityIdFormat.W3C);

        if (activity.Id is null)
        {
            throw new ArgumentException("The activity ID property cannot be null.", nameof(activity.Id));
        }

        // The activity context is written to the field value, as if it has the following Slice definition
        //
        // compact struct BaggageEntry
        // {
        //    string key;
        //    string value;
        // }
        //
        // Sequence<BaggageEntry> Baggage;
        //
        // compact struct ActivityContext
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
        //
        // Baggage is modeled as a sequence rather than a dictionary because Activity.Baggage allows
        // duplicate keys: encoding as a Slice dictionary could produce bytes that a strict dictionary
        // decoder in another language would reject.

        // W3C traceparent binary encoding (1 byte version, 16 bytes trace-ID, 8 bytes span-ID,
        // 1 byte flags) https://www.w3.org/TR/trace-context/#traceparent-header-field-values
        encoder.EncodeUInt8(0);

        Span<byte> buffer = stackalloc byte[16];
        activity.TraceId.CopyTo(buffer);
        encoder.WriteByteSpan(buffer);
        activity.SpanId.CopyTo(buffer[0..8]);
        encoder.WriteByteSpan(buffer[0..8]);
        encoder.EncodeUInt8((byte)activity.ActivityTraceFlags);

        // TraceState encoded as a string
        encoder.EncodeString(activity.TraceStateString ?? "");

        // Baggage encoded as a Sequence<BaggageEntry>, clipped to MaxBaggageEntries. Activity.Baggage has
        // no documented iteration order, so which entries we retain when clipping is unspecified; the W3C
        // Baggage spec permits dropping list-members in any order.
        KeyValuePair<string, string?>[] baggage = activity.Baggage.Take(MaxBaggageEntries).ToArray();
        encoder.EncodeSequence(
            baggage,
            (ref SliceEncoder encoder, KeyValuePair<string, string?> entry) =>
            {
                encoder.EncodeString(entry.Key);
                encoder.EncodeString(entry.Value ?? "");
            });
    }
}
