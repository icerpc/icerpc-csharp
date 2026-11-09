// Copyright (c) ZeroC, Inc.

using System.Net;

namespace IceRpc.Transports.Slic.Internal;

internal class SlicListener : IListener<IMultiplexedConnection>
{
    public TransportAddress TransportAddress => _duplexListener.TransportAddress;

    private readonly IListener<IDuplexConnection> _duplexListener;
    private readonly TimeSpan _idleTimeout;
    private readonly int _initialStreamWindowSize;
    private readonly int _maxOutstandingPongs;
    private readonly int _maxStreamFrameSize;
    private readonly MultiplexedConnectionOptions _options;
    private readonly int _pauseWriterThreshold;

    public async Task<(IMultiplexedConnection, EndPoint)> AcceptAsync(CancellationToken cancellationToken)
    {
        (IDuplexConnection duplexConnection, EndPoint remoteNetworkAddress) =
            await _duplexListener.AcceptAsync(cancellationToken).ConfigureAwait(false);
        var connection = new SlicConnection(
            duplexConnection,
            _options,
            _idleTimeout,
            _initialStreamWindowSize,
            _maxOutstandingPongs,
            _maxStreamFrameSize,
            _pauseWriterThreshold,
            isServer: true);
        return (connection, remoteNetworkAddress);
    }

    public ValueTask DisposeAsync() => _duplexListener.DisposeAsync();

    internal SlicListener(
        IListener<IDuplexConnection> duplexListener,
        MultiplexedConnectionOptions options,
        TimeSpan idleTimeout,
        int initialStreamWindowSize,
        int maxOutstandingPongs,
        int maxStreamFrameSize,
        int pauseWriterThreshold)
    {
        _duplexListener = duplexListener;
        _idleTimeout = idleTimeout;
        _initialStreamWindowSize = initialStreamWindowSize;
        _maxOutstandingPongs = maxOutstandingPongs;
        _maxStreamFrameSize = maxStreamFrameSize;
        _options = options;
        _pauseWriterThreshold = pauseWriterThreshold;
    }
}
