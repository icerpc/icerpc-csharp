// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal;

internal class SlicListener : IListener<IMultiplexedNetworkConnection>
{
    private readonly IMultiplexedStreamErrorCodeConverter _errorCodeConverter;
    private readonly IListener<ISimpleNetworkConnection> _simpleListener;
    private readonly SlicTransportOptions _slicOptions;

    public Endpoint Endpoint => _simpleListener.Endpoint;

    public async Task<IMultiplexedNetworkConnection> AcceptAsync() =>
        new SlicNetworkConnection(
            await _simpleListener.AcceptAsync().ConfigureAwait(false),
            isServer: true,
            _errorCodeConverter,
            _slicOptions);

    public void Dispose() => _simpleListener.Dispose();

    internal SlicListener(IListener<ISimpleNetworkConnection> simpleListener, SlicTransportOptions slicOptions)
    {
        _errorCodeConverter = simpleListener.Endpoint.Protocol.MultiplexedStreamErrorCodeConverter ??
            throw new NotSupportedException(
                $"cannot create Slic listener for protocol {simpleListener.Endpoint.Protocol}");

        _simpleListener = simpleListener;
        _slicOptions = slicOptions;
    }
}
