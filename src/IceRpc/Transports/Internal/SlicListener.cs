// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net;

namespace IceRpc.Transports.Internal;

internal class SlicListener : IListener<IMultiplexedConnection>
{
    public ServerAddress ServerAddress => _duplexListener.ServerAddress;

    private readonly IListener<IDuplexConnection> _duplexListener;
    private readonly MultiplexedConnectionOptions _options;
    private readonly SlicTransportOptions _slicOptions;

    public async Task<(IMultiplexedConnection, EndPoint)> AcceptAsync(CancellationToken cancellationToken)
    {
        (IDuplexConnection duplexConnection, EndPoint remoteNetworkAddress) =
            await _duplexListener.AcceptAsync(cancellationToken).ConfigureAwait(false);
        return (new SlicConnection(duplexConnection, _options, _slicOptions, isServer: true), remoteNetworkAddress);
    }

    public void Dispose() => _duplexListener.Dispose();

    public Task ListenAsync(CancellationToken cancellationToken) => _duplexListener.ListenAsync(cancellationToken);

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
