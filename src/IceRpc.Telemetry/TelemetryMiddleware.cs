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
/// <remarks>The activities are only created for requests using the icerpc protocol. The activity records the outcome
/// of the dispatch. The <c>rpc.status_code</c> tag holds the status code of the response returned by the dispatch.
/// When the dispatch throws an exception, this tag holds the status code of the failure response the caller receives,
/// given by <see cref="DispatchException.FromException" />. A status code that reports a problem with the request
/// (<see cref="StatusCode.ApplicationError" />, <see cref="StatusCode.NotFound" />,
/// <see cref="StatusCode.InvalidData" />, <see cref="StatusCode.TruncatedPayload" /> and
/// <see cref="StatusCode.Unauthorized" />) leaves the activity status unset. Any other failure status code sets the
/// activity status to <see cref="ActivityStatusCode.Error" /> and the <c>error.type</c> tag identifies the failure. A
/// cancellation by the token passed to <see cref="DispatchAsync" /> is not a failure: the <c>icerpc.canceled</c> tag
/// is set to <see langword="true" />, the activity status stays unset, and the <c>rpc.status_code</c> tag is not set
/// since the caller receives no response.</remarks>
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
            try
            {
                OutgoingResponse response = await _next.DispatchAsync(request, cancellationToken).ConfigureAwait(false);
                activity.SetTag("rpc.status_code", response.StatusCode.ToString());
                if (IsServerError(response.StatusCode))
                {
                    activity.SetTag("error.type", TelemetryInterceptor.GetErrorType(response.StatusCode));
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
                DispatchException dispatchException = DispatchException.FromException(exception);
                activity.SetTag("rpc.status_code", dispatchException.StatusCode.ToString());
                if (IsServerError(dispatchException.StatusCode))
                {
                    // Like a returned response, a dispatch exception that FromException returns unchanged identifies
                    // the failure by its status code. Any other exception is identified by its type.
                    activity.SetTag(
                        "error.type",
                        ReferenceEquals(dispatchException, exception) ?
                            TelemetryInterceptor.GetErrorType(dispatchException.StatusCode) :
                            exception.GetType().FullName);
                    activity.SetStatus(ActivityStatusCode.Error, exception.Message);
                }
                throw;
            }
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

        // Decode the baggage sequence, silently clipping to MaxBaggageEntries. OpenTelemetry SDKs
        // follow a strict no-throw policy for observability operations: losing a piece of contextual
        // metadata is less damaging than failing the RPC, and silent clipping matches the behavior of
        // OpenTelemetry .NET's and Python's BaggagePropagator on incoming headers. Only the first
        // MaxBaggageEntries entries are read from the buffer; the remainder is left unconsumed.
        //
        // Activity.Baggage's enumeration order is undocumented, so duplicate-key resolution across the
        // wire is inherently undefined on both ends — we don't attempt to preserve it.
        int count = decoder.DecodeSize();
        int kept = Math.Min(count, TelemetryInterceptor.MaxBaggageEntries);

        for (int i = 0; i < kept; i++)
        {
            string key = decoder.DecodeString();
            string value = decoder.DecodeString();
            activity.AddBaggage(key, value);
        }
    }

    /// <summary>Checks whether a status code reports a failure of the server or the target service, as opposed to a
    /// problem with the request.</summary>
    private static bool IsServerError(StatusCode statusCode) =>
        statusCode switch
        {
            StatusCode.Ok or
            StatusCode.ApplicationError or
            StatusCode.NotFound or
            StatusCode.InvalidData or
            StatusCode.TruncatedPayload or
            StatusCode.Unauthorized => false,
            _ => true
        };
}
