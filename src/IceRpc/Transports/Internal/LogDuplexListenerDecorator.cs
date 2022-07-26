// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal;

internal sealed class LogDuplexListenerDecorator : IDuplexListener
{
    private readonly IDuplexListener _decoratee;
    private readonly ILogger _logger;

    public Endpoint Endpoint => _decoratee.Endpoint;

    public async Task<IDuplexConnection> AcceptAsync()
    {
        IDuplexConnection connection;
        try
        {
            connection = await _decoratee.AcceptAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // We assume the decoratee is shut down which should not result in an error message.
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogListenerAcceptException(exception, _decoratee.Endpoint);
            throw;
        }

        _logger.LogListenerAccept(_decoratee.Endpoint);
        return new LogDuplexConnectionDecorator(connection, _logger);
    }

    public void Dispose()
    {
        _decoratee.Dispose();
        _logger.LogListenerDispose(_decoratee.Endpoint);
    }

    public override string? ToString() => _decoratee.ToString();

    internal LogDuplexListenerDecorator(IDuplexListener decoratee, ILogger logger)
    {
        _decoratee = decoratee;
        _logger = logger;
    }
}
