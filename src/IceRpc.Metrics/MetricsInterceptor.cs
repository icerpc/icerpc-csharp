// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Metrics.Internal;

namespace IceRpc.Metrics;

/// <summary>An interceptor that publishes invocation metrics.</summary>
public class MetricsInterceptor : IInvoker
{
    private readonly InvocationEventSource _eventSource;
    private readonly IInvoker _next;

    /// <inheritdoc/>
    public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
    {
        long startTime = _eventSource.RequestStart(request);
        var resultType = ResultType.Failure;
        try
        {
            IncomingResponse response = await _next.InvokeAsync(request, cancel).ConfigureAwait(false);
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

    /// <summary>Constructs a metrics interceptor.</summary>
    /// <param name="next">The next invoker in the invocation pipeline.</param>
    /// <param name="eventSource">The invocation event source used to publish the metrics events.</param>
    internal MetricsInterceptor(IInvoker next, InvocationEventSource eventSource)
    {
        _next = next;
        _eventSource = eventSource;
    }
}
