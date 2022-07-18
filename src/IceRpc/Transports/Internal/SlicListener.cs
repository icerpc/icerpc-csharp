// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal;

internal class SlicListener : IMultiplexedListener
{
    private readonly IMultiplexedStreamErrorCodeConverter _errorCodeConverter;
    private readonly IDuplexListener _duplexListener;
    private readonly SlicTransportOptions _slicOptions;

    public Endpoint Endpoint => _duplexListener.Endpoint;

    public async Task<IMultiplexedConnection> AcceptAsync() =>
        new SlicMultiplexedConnection(
            await _duplexListener.AcceptAsync().ConfigureAwait(false),
            isServer: true,
            _errorCodeConverter,
            _slicOptions);

    public void Dispose() => _duplexListener.Dispose();

    internal SlicListener(
        IDuplexListener duplexListener,
        SlicTransportOptions slicOptions)
    {
        _errorCodeConverter = duplexListener.Endpoint.Protocol.MultiplexedStreamErrorCodeConverter ??
            throw new NotSupportedException(
                $"cannot create Slic listener for protocol {duplexListener.Endpoint.Protocol}");

        _duplexListener = duplexListener;
        _slicOptions = slicOptions;
    }
}
