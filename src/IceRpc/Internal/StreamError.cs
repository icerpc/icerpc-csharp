// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    /// <summary>Error codes for stream errors.</summary>
    // TODO: XXX Review unused values
    internal enum MultiplexedStreamError : byte
    {
        /// <summary>The stream was aborted because the invocation was canceled.</summary>
        InvocationCanceled,

        /// <summary>The stream was aborted because the dispatch was canceled.</summary>
        DispatchCanceled,

        /// <summary>The stream was aborted because the connection was shutdown.</summary>
        ConnectionShutdown,

        /// <summary>The stream was aborted because the connection was shutdown by the peer.</summary>
        ConnectionShutdownByPeer,

        /// <summary>The stream was aborted because the connection was aborted.</summary>
        ConnectionAborted,

        /// <summary>Stream data is not expected.</summary>
        UnexpectedStreamData,
    }

    internal static class PipeExtensions
    {
        internal static void Complete(this PipeReader reader, MultiplexedStreamError errorCode) =>
            reader.Complete(new MultiplexedStreamAbortedException((byte)errorCode));

        internal static ValueTask CompleteAsync(this PipeReader reader, MultiplexedStreamError errorCode) =>
            reader.CompleteAsync(new MultiplexedStreamAbortedException((byte)errorCode));

        internal static void Complete(this PipeWriter writer, MultiplexedStreamError errorCode) =>
            writer.Complete(new MultiplexedStreamAbortedException((byte)errorCode));

        internal static ValueTask CompleteAsync(this PipeWriter writer, MultiplexedStreamError errorCode) =>
            writer.CompleteAsync(new MultiplexedStreamAbortedException((byte)errorCode));
    }

    internal static class MultiplexedStreamAbortedExceptionExtensions
    {
        internal static Exception ToProtocolException(this MultiplexedStreamAbortedException exception) =>
            (MultiplexedStreamError)exception.ErrorCode switch
            {
                MultiplexedStreamError.DispatchCanceled =>
                    new OperationCanceledException("dispatch canceled by peer"),
                MultiplexedStreamError.ConnectionShutdownByPeer =>
                    new ConnectionClosedException("connection shutdown by peer"),
                MultiplexedStreamError.ConnectionShutdown => new OperationCanceledException("connection shutdown"),
                _ => new ArgumentException("invalid stream error {exception.ErrorCode}"),
            };
    }
}
