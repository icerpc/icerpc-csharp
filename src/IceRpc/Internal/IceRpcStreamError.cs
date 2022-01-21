// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    /// <summary>IceRPC protocol stream error codes.</summary>
    /// TODO: make these public?
    internal enum IceRpcStreamError : int
    {
        /// <summary>The stream was aborted because the invocation was canceled.</summary>
        InvocationCanceled,

        /// <summary>The stream was aborted because the dispatch was canceled.</summary>
        DispatchCanceled,

        /// <summary>The stream was aborted because the connection was shutdown.</summary>
        ConnectionShutdown,

        /// <summary>The stream was aborted because the connection was shutdown by the peer.</summary>
        ConnectionShutdownByPeer,
    }

    internal static class IceRpcStreamErrorExtensions
    {
        internal static MultiplexedStreamAbortedException ToException(this IceRpcStreamError error) =>
            new(MultiplexedStreamErrorKind.Protocol, (int)error);

        internal static Exception ToIceRpcException(this MultiplexedStreamAbortedException exception)
        {
            if (exception.ErrorKind == MultiplexedStreamErrorKind.Protocol)
            {
                return (IceRpcStreamError)exception.ErrorCode switch
                {
                    IceRpcStreamError.InvocationCanceled =>
                        new ConnectionClosedException("invocation canceled by peer"),
                    IceRpcStreamError.DispatchCanceled =>
                        new OperationCanceledException("dispatch canceled by peer"),
                    IceRpcStreamError.ConnectionShutdownByPeer =>
                        new ConnectionClosedException("connection shutdown by peer"),
                    IceRpcStreamError.ConnectionShutdown =>
                        new OperationCanceledException("connection shutdown"),
                    _ => new OperationCanceledException("unexpected protocol error code {exception.ErrorCode}"),
                };
            }
            else
            {
                return exception;
            }
        }
    }
}
