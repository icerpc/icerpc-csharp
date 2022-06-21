// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal;

internal class BidirConnection : IConnection
{
    public bool IsResumable => true;

    public NetworkConnectionInformation? NetworkConnectionInformation => Decoratee.NetworkConnectionInformation;

    public Protocol Protocol => Decoratee.Protocol;

    internal IConnection Decoratee { get; set; }

    public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel) =>
        Decoratee.InvokeAsync(request, cancel);

    public void OnClose(Action<Exception> callback) => Decoratee.OnClose(callback);

    internal BidirConnection(IConnection decoratee) => Decoratee = decoratee;
}
