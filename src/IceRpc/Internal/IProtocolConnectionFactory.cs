// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal
{
    internal interface IProtocolConnectionFactory<T> where T : INetworkConnection
    {
        /// <summary>Creates a protocol connection from a network connection.</summary>
        // TODO: refactor this API
        Task<IProtocolConnection> CreateProtocolConnectionAsync(
            T networkConnection,
            NetworkConnectionInformation connectionInformation,
            Configure.IceProtocolOptions? iceProtocolOptions,
            IDictionary<ConnectionFieldKey, OutgoingFieldValue> localFields,
            bool isServer,
            CancellationToken cancel);
    }
}
