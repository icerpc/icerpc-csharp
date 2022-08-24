// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Metrics.Internal;

namespace IceRpc.Metrics;

/// <summary>A middleware that publishes dispatch metrics using a dispatch event source.</summary>
public class MetricsMiddleware : IDispatcher
{
    private readonly DispatchEventSource _eventSource;
    private readonly IDispatcher _next;

    /// <summary>Constructs a metrics middleware.</summary>
    /// <param name="next">The next dispatcher in the dispatch pipeline.</param>
    public MetricsMiddleware(IDispatcher next)
        : this(next, DispatchEventSource.Log)
    {
    }

    /// <inheritdoc/>
    public async ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken)
    {
        long startTime = _eventSource.RequestStart(request);
        var resultType = ResultType.Failure;
        try
        {
            OutgoingResponse response = await _next.DispatchAsync(request, cancellationToken).ConfigureAwait(false);
            resultType = response.ResultType;
            return response;
        }
        catch (OperationCanceledException)
        {
            _eventSource.RequestCancel(request);
            throw;
        }
        catch (Exception ex)
        {
            _eventSource.RequestFailure(request, ex);
            throw;
        }
        finally
        {
            _eventSource.RequestStop(request, resultType, startTime);
        }
    }

    /// <summary>Constructs a metrics middleware.</summary>
    /// <param name="next">The next dispatcher in the dispatch pipeline.</param>
    /// <param name="eventSource">The dispatch event source used to publish the metrics events.</param>
    internal MetricsMiddleware(IDispatcher next, DispatchEventSource eventSource)
    {
        _next = next;
        _eventSource = eventSource;
    }
}
