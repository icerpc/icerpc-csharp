// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Metrics;

/// <summary>An interceptor that publishes invocation metrics.</summary>
public class MetricsInterceptor : IInvoker
{
    private readonly InvocationEventSource _eventSource;
    private readonly IInvoker _next;

    /// <summary>Constructs a metrics interceptor.</summary>
    /// <param name="next">The next invoker in the invocation pipeline.</param>
    /// <param name="eventSource">The invocation event source used to publish the metrics events.</param>
    public MetricsInterceptor(IInvoker next, InvocationEventSource eventSource)
    {
        _next = next;
        _eventSource = eventSource;
    }

    /// <inheritdoc/>
    public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel)
    {
        TimeSpan startTime = _eventSource.RequestStart(request);
        try
        {
            IncomingResponse response = await _next.InvokeAsync(request, cancel).ConfigureAwait(false);
            if (response.ResultType != ResultType.Success)
            {
                _eventSource.RequestFailure(request, response.ResultType);
            }
            return response;
        }
        catch (OperationCanceledException)
        {
            _eventSource.RequestCanceled(request);
            throw;
        }
        catch (Exception ex)
        {
            _eventSource.RequestException(request, ex);
            throw;
        }
        finally
        {
            _eventSource.RequestStop(request, startTime);
        }
    }
}
