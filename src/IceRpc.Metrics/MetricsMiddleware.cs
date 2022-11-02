// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Metrics.Internal;
using System.Diagnostics.Metrics;

namespace IceRpc.Metrics;

/// <summary>A middleware that publishes dispatch metrics using a <see cref="Meter"/> named "IceRpc.Dispatch".
/// </summary>
public class MetricsMiddleware : IDispatcher
{
    private readonly IDispatcher _next;

    /// <summary>Constructs a metrics middleware.</summary>
    /// <param name="next">The next dispatcher in the dispatch pipeline.</param>
    public MetricsMiddleware(IDispatcher next) => _next = next;

    /// <inheritdoc/>
    public async ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancellationToken)
    {
        DispatchMetrics.RequestStart();
        try
        {
            return await _next.DispatchAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            DispatchMetrics.RequestCancel();
            throw;
        }
        catch
        {
            DispatchMetrics.RequestFailure();
            throw;
        }
        finally
        {
            DispatchMetrics.RequestStop();
        }
    }
}
