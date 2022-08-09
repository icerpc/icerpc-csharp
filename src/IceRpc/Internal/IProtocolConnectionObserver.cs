// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;

namespace IceRpc.Internal;

/// <summary>An observer for <see cref="ProtocolConnection"/>.</summary>
internal interface IProtocolConnectionObserver
{
    /// <summary>The connect operation started.</summary>
    void ConnectStart(ServerAddress serverAddress);

    /// <summary>The connection establishment was successful.</summary>
    void Connected(ServerAddress serverAddress, TransportConnectionInformation connectionInformation);

    /// <summary>The connection establishment failed.</summary>
    void ConnectException(Exception exception, ServerAddress serverAddress);

    /// <summary>The connection was disposed.</summary>
    void Disposed(ServerAddress serverAddress);

    /// <summary>The connection was shut down successfully.</summary>
    void ShutdownComplete(string message, ServerAddress serverAddress);

    /// <summary>The connection shutdown failed.</summary>
    void ShutdownException(Exception exception, string message, ServerAddress serverAddress);
}
