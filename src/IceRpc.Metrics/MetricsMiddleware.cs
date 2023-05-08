// Copyright (c) ZeroC, Inc.

using IceRpc.Extensions.DependencyInjection;
using IceRpc.Metrics.Internal;
using System.Diagnostics.Metrics;

namespace IceRpc.Metrics;

/// <summary>A middleware that publishes dispatch metrics using a singleton meter named "IceRpc.Dispatch".
/// </summary>
/// <seealso cref="Meter"/>
/// <seealso cref="MetricsRouterExtensions.UseMetrics(Router)"/>
/// <seealso cref="MetricsDispatcherBuilderExtensions.UseMetrics(IDispatcherBuilder)"/>
public class MetricsMiddleware : IDispatcher
{
    private readonly IDispatcher _next;
    private readonly DispatchMetrics _dispatchMetrics;

    /// <summary>Constructs a metrics middleware.</summary>
    /// <param name="next">The next dispatcher in the dispatch pipeline.</param>
    public MetricsMiddleware(IDispatcher next)
        : this(next, DispatchMetrics.Instance)
    {
    }

    /// <inheritdoc/>
    public async ValueTask<OutgoingResponse> DispatchAsync(
        IncomingRequest request,
        CancellationToken cancellationToken)
    {
        _dispatchMetrics.RequestStart();
        try
        {
            return await _next.DispatchAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _dispatchMetrics.RequestCancel();
            throw;
        }
        catch
        {
            _dispatchMetrics.RequestFailure();
            throw;
        }
        finally
        {
            _dispatchMetrics.RequestStop();
        }
    }

    internal MetricsMiddleware(IDispatcher next, DispatchMetrics dispatchMetrics)
    {
        _next = next;
        _dispatchMetrics = dispatchMetrics;
    }
}
