// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Metrics.Internal;
using System.Diagnostics.Metrics;

namespace IceRpc.Metrics;

/// <summary>An interceptor that publishes invocation metrics using a <see cref="Meter"/> named "IceRpc.Invocation".
/// </summary>
public class MetricsInterceptor : IInvoker
{
    private readonly IInvoker _next;

    /// <summary>Constructs a metrics interceptor.</summary>
    /// <param name="next">The next invoker in the invocation pipeline.</param>
    public MetricsInterceptor(IInvoker next) => _next = next;

    /// <inheritdoc/>
    public async Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancellationToken)
    {
        InvocationMetrics.RequestStart();
        try
        {
            return await _next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            InvocationMetrics.RequestCancel();
            throw;
        }
        catch
        {
            InvocationMetrics.RequestFailure();
            throw;
        }
        finally
        {
            InvocationMetrics.RequestStop();
        }
    }
}
