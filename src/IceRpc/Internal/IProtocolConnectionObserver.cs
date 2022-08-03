// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal;

/// <summary>An observer for <see cref="ProtocolConnection"/>.</summary>
internal interface IProtocolConnectionObserver
{
    void Connected(ServerAddress serverAddress, TransportConnectionInformation connectionInformation);

    void ConnectException(Exception exception, ServerAddress serverAddress);

    void Disposed(ServerAddress serverAddress);

    void ShutDown(string message, ServerAddress serverAddress);
}
