// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal;

internal sealed class LogDuplexListenerDecorator : IDuplexListener
{
    public Endpoint Endpoint => _decoratee.Endpoint;

    private const string Kind = "Duplex";
    private readonly IDuplexListener _decoratee;
    private readonly ILogger _logger;

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
            _logger.LogListenerAcceptException(exception, Kind, _decoratee.Endpoint);
            throw;
        }

        _logger.LogListenerAccept(Kind, _decoratee.Endpoint);
        return connection;
    }

    public void Dispose()
    {
        _decoratee.Dispose();
        _logger.LogListenerDispose(Kind, _decoratee.Endpoint);
    }

    public override string? ToString() => _decoratee.ToString();

    internal LogDuplexListenerDecorator(IDuplexListener decoratee, ILogger logger)
    {
        _decoratee = decoratee;
        _logger = logger;
    }
}
