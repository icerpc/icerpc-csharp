// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal;

internal class SlicListener : IMultiplexedListener
{
    private readonly IDuplexListener _duplexListener;
    private readonly MultiplexedConnectionOptions _options;
    private readonly SlicTransportOptions _slicOptions;

    public ServerAddress ServerAddress => _duplexListener.ServerAddress;

    public async Task<IMultiplexedConnection> AcceptAsync() =>
        new SlicConnection(
            await _duplexListener.AcceptAsync().ConfigureAwait(false),
            _options,
            _slicOptions,
            isServer: true);

    public void Dispose() => _duplexListener.Dispose();

    internal SlicListener(
        IDuplexListener duplexListener,
        MultiplexedConnectionOptions options,
        SlicTransportOptions slicOptions)
    {
        _duplexListener = duplexListener;
        _options = options;
        _slicOptions = slicOptions;
    }
}
