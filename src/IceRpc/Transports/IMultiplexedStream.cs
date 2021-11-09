// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>Error codes for stream errors.</summary>
    public enum StreamError : byte
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

        /// <summary>The stream was aborted.</summary>
        StreamAborted
    }

    /// <summary>A multiplexed stream enables byte data exchange over a multiplexed transport.</summary>
    public interface IMultiplexedStream
    {
        /// <summary>The stream ID.</summary>
        /// <exception cref="InvalidOperationException">Raised if the stream is not started. Local streams are not
        /// started until the first <see cref="WriteAsync"/> call. A remote stream is always started.</exception>
        long Id { get; }

        /// <summary>Returns <c>true</c> if the stream is a bidirectional stream, <c>false</c> otherwise.</summary>
        bool IsBidirectional { get; }

        /// <summary>Returns <c>true</c> if the local stream is started, <c>false</c> otherwise.</summary>
        bool IsStarted { get; }

        /// <summary>Sets the action which is called when the stream is reset.</summary>
        Action? ShutdownAction { get; set; }

        /// <summary>The transport header sentinel. Transport implementations that need to add an additional header to
        /// transmit data over the stream can provide the header data here. This can improve performance by reducing the
        /// number of allocations since the protocol implementation  will allocate buffer space for both the transport
        /// header and the protocol header. If a header is returned here, the implementation of the <see
        /// cref="WriteAsync"/> method should expect this header to be set at the start of the first buffer.</summary>
        ReadOnlyMemory<byte> TransportHeader { get; }

        /// <summary>Aborts the stream read side.</summary>
        /// <param name="errorCode">The reason of the abort.</param>
        void AbortRead(StreamError errorCode);

        /// <summary>Aborts the stream write side.</summary>
        /// <param name="errorCode">The reason of the abort.</param>
        void AbortWrite(StreamError errorCode);

        /// <summary>Gets a <see cref="System.IO.Stream"/> to allow using this stream using a <see
        /// cref="System.IO.Stream"/></summary>
        /// <returns>The <see cref="System.IO.Stream"/> object.</returns>
        System.IO.Stream AsByteStream();

        /// <summary>Reads data from the stream.</summary>
        /// <param name="buffer">The buffer that holds the read data.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The number of bytes read.</returns>
        ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel);

        /// <summary>Waits for the stream shutdown completion.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A task that completes once the stream is shutdown.</returns>
        Task WaitForShutdownAsync(CancellationToken cancel);

        /// <summary>Writes data over the stream.</summary>
        /// <param name="buffers">The buffers containing the data to write.</param>
        /// <param name="endStream"><c>true</c> if no more data will be written over this stream, <c>false</c>
        /// otherwise.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A value task that completes once the buffers are written.</returns>
        ValueTask WriteAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, bool endStream, CancellationToken cancel);
    }
}
