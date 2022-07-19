// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal;

internal class SlicListener : IMultiplexedListener
{
    private readonly IDuplexListener _duplexListener;
    private readonly MultiplexedListenerOptions _options;
    private readonly SlicTransportOptions _slicOptions;

    public Endpoint Endpoint => _duplexListener.Endpoint;

    public async Task<IMultiplexedConnection> AcceptAsync() =>
        new SlicMultiplexedConnection(
            await _duplexListener.AcceptAsync().ConfigureAwait(false),
            isServer: true,
            _options.ServerConnectionOptions,
            _slicOptions);

    public void Dispose() => _duplexListener.Dispose();

    internal SlicListener(
        IDuplexListener duplexListener,
        MultiplexedListenerOptions options,
        SlicTransportOptions slicOptions)
    {
        _duplexListener = duplexListener;
        _options = options;
        _slicOptions = slicOptions;
    }
}
