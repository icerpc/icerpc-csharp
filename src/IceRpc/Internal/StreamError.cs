// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    /// <summary>Error codes for stream errors.</summary>
    internal enum MultiplexedStreamError : byte
    {
        /// <summary>The stream was aborted because the invocation was canceled.</summary>
        InvocationCanceled,

        /// <summary>The stream was aborted because the dispatch was canceled.</summary>
        DispatchCanceled,

        /// <summary>Streaming was canceled by the reader.</summary>
        StreamingCanceledByReader,

        /// <summary>Streaming was canceled by the writer.</summary>
        StreamingCanceledByWriter,

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

        internal static void Complete(this PipeWriter writer, MultiplexedStreamError errorCode) =>
            writer.Complete(new MultiplexedStreamAbortedException((byte)errorCode));
    }
}
