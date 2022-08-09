// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal;

internal class SlicListener : IListener<IMultiplexedConnection>
{
    public ServerAddress ServerAddress => _duplexListener.ServerAddress;

    private readonly IListener<IDuplexConnection> _duplexListener;
    private readonly MultiplexedConnectionOptions _options;
    private readonly SlicTransportOptions _slicOptions;

    public async Task<IMultiplexedConnection> AcceptAsync() =>
        new SlicConnection(
            await _duplexListener.AcceptAsync().ConfigureAwait(false),
            _options,
            _slicOptions,
            isServer: true);

    public void Dispose() => _duplexListener.Dispose();

    internal SlicListener(
        IListener<IDuplexConnection> duplexListener,
        MultiplexedConnectionOptions options,
        SlicTransportOptions slicOptions)
    {
        _duplexListener = duplexListener;
        _options = options;
        _slicOptions = slicOptions;
    }
}
