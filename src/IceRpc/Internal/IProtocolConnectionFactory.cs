// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Buffers;

namespace IceRpc.Internal;

internal interface IProtocolConnectionFactory<T, TOptions> where T : INetworkConnection where TOptions : class
{
    /// <summary>Creates a protocol connection over a connected network connection.</summary>
    Task<IProtocolConnection> CreateProtocolConnectionAsync(
        T networkConnection,
        NetworkConnectionInformation connectionInformation,
        IDispatcher dispatcher,
        Action<Dictionary<ConnectionFieldKey, ReadOnlySequence<byte>>>? onConnect,
        bool isServer,
        TOptions? protocolOptions,
        CancellationToken cancel);
}
