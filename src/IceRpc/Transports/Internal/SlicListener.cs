// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal;

internal class SlicListener : IListener<IMultiplexedTransportConnection>
{
    private readonly IMultiplexedStreamErrorCodeConverter _errorCodeConverter;
    private readonly IListener<ISingleStreamTransportConnection> _singleStreamListener;
    private readonly SlicTransportOptions _slicOptions;

    public Endpoint Endpoint => _singleStreamListener.Endpoint;

    public async Task<IMultiplexedTransportConnection> AcceptAsync() =>
        new SlicTransportConnection(
            await _singleStreamListener.AcceptAsync().ConfigureAwait(false),
            isServer: true,
            _errorCodeConverter,
            _slicOptions);

    public void Dispose() => _singleStreamListener.Dispose();

    internal SlicListener(
        IListener<ISingleStreamTransportConnection> singleStreamListener,
        SlicTransportOptions slicOptions)
    {
        _errorCodeConverter = singleStreamListener.Endpoint.Protocol.MultiplexedStreamErrorCodeConverter ??
            throw new NotSupportedException(
                $"cannot create Slic listener for protocol {singleStreamListener.Endpoint.Protocol}");

        _singleStreamListener = singleStreamListener;
        _slicOptions = slicOptions;
    }
}
