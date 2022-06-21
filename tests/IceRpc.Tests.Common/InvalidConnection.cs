// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Tests.Common;

/// <summary>Provides invalid shared connections. Each connection is protocol-specific.</summary>
public static class InvalidConnection
{
    private class Connection : IConnection
    {
        public bool IsResumable => throw new NotImplementedException();

        public NetworkConnectionInformation? NetworkConnectionInformation => throw new NotImplementedException();

        public Protocol Protocol { get; }

        public Task<IncomingResponse> InvokeAsync(OutgoingRequest request, CancellationToken cancel) =>
            throw new NotImplementedException();

        public void OnClose(Action<Exception> callback) => throw new NotImplementedException();

        internal Connection(Protocol protocol) => Protocol = protocol;
    }

    /// <summary>An invalid ice connection.</summary>
    public static IConnection Ice { get; } = new Connection(Protocol.Ice);

    /// <summary>An invalid icerpc connection.</summary>
    public static IConnection IceRpc { get; } = new Connection(Protocol.IceRpc);

    /// <summary>Returns the invalid connection for the given protocol.</summary>
    public static IConnection ForProtocol(Protocol protocol) => protocol == Protocol.Ice ? Ice : IceRpc;
}
