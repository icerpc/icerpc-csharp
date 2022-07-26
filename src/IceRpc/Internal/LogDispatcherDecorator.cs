// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Internal;

internal class LogDispatcherDecorator : IDispatcher
{
    private readonly IDispatcher _decoratee;
    private readonly ILogger _logger;

    public ValueTask<OutgoingResponse> DispatchAsync(IncomingRequest request, CancellationToken cancel)
    {
        return _logger.IsEnabled(LogLevel.Debug) ? PerformDispatchAsync() : _decoratee.DispatchAsync(request, cancel);

        async ValueTask<OutgoingResponse> PerformDispatchAsync()
        {
            using IDisposable _ = _logger.StartConnectionDispatchScope(request);

            try
            {
                OutgoingResponse response = await _decoratee.DispatchAsync(request, cancel).ConfigureAwait(false);

                _logger.LogConnectionDispatch(response.ResultType);

                return response;
            }
            catch (Exception exception)
            {
                _logger.LogConnectionDispatchException(exception);
                throw;
            }
        }
    }

    internal LogDispatcherDecorator(IDispatcher decoratee, ILogger logger)
    {
        _decoratee = decoratee;
        _logger = logger;
    }
}
