// Copyright (c) ZeroC, Inc.

using IceRpc.Extensions.DependencyInjection;
using IceRpc.Metrics.Internal;
using System.Diagnostics.Metrics;

namespace IceRpc.Metrics;

/// <summary>An interceptor that publishes invocation metrics using a singleton meter named "IceRpc.Invocation".
/// </summary>
/// <seealso cref="Meter"/>
/// <seealso cref="MetricsPipelineExtensions"/>
/// <seealso cref="MetricsInvokerBuilderExtensions"/>
public class MetricsInterceptor : IInvoker
{
    private readonly IInvoker _next;
    private readonly InvocationMetrics _invocationMetrics;

    /// <summary>Constructs a metrics interceptor.</summary>
    /// <param name="next">The next invoker in the invocation pipeline.</param>
    public MetricsInterceptor(IInvoker next)
        : this(next, InvocationMetrics.Instance)
    {
    }

    /// <inheritdoc/>
    public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken)
    {
        _invocationMetrics.RequestStart();
        try
        {
            IncomingResponse response = await _next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
            // A non-OK status code means the server returned a failure response.
            if (response.StatusCode != StatusCode.Ok)
            {
                _invocationMetrics.RequestFailure();
            }
            return response;
        }
        catch (OperationCanceledException)
        {
            _invocationMetrics.RequestCancel();
            throw;
        }
        catch
        {
            _invocationMetrics.RequestFailure();
            throw;
        }
        finally
        {
            _invocationMetrics.RequestStop();
        }
    }

    internal MetricsInterceptor(IInvoker next, InvocationMetrics invocationMetrics)
    {
        _next = next;
        _invocationMetrics = invocationMetrics;
    }
}
