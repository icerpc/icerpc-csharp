// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal;

internal class BidirConnection : IConnection
{
    private readonly IConnection _decoratee;

    public bool IsResumable => true;

    public NetworkConnectionInformation? NetworkConnectionInformation => _decoratee.NetworkConnectionInformation;

    public Protocol Protocol => _decoratee.Protocol;

    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel) =>
        _decoratee.InvokeAsync(request, cancel);

    public void OnClose(Action<IConnection, Exception> callback) => _decoratee.OnClose(callback);

    internal BidirConnection(IConnection decoratee) => _decoratee = decoratee;
}
