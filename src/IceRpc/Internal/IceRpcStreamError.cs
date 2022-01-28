// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Diagnostics;

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
            Debug.Assert(exception.ErrorKind == MultiplexedStreamErrorKind.Protocol);
            return (IceRpcStreamError)exception.ErrorCode switch
            {
                IceRpcStreamError.InvocationCanceled =>
                    new ConnectionClosedException("invocation canceled by peer", exception),
                IceRpcStreamError.DispatchCanceled =>
                    new OperationCanceledException("dispatch canceled by peer", exception),
                IceRpcStreamError.ConnectionShutdownByPeer =>
                    new ConnectionClosedException("connection shutdown by peer", exception),
                IceRpcStreamError.ConnectionShutdown =>
                    new OperationCanceledException("connection shutdown", exception),
                _ => new OperationCanceledException(
                    "unexpected protocol error {exception.ErrorCode}",
                    exception),
            };
        }
    }
}
