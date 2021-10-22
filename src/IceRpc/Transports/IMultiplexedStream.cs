// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>Raised if a stream is aborted.</summary>
    public class StreamAbortedException : Exception
    {
        /// <summary>The stream error code.</summary>
        public StreamError ErrorCode { get; }

        /// <summary>Constructs a new exception.</summary>
        /// <param name="errorCode">The stream error code.</param>
        public StreamAbortedException(StreamError errorCode) :
            base($"stream aborted with error code {errorCode}") => ErrorCode = errorCode;
    }

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
        /// <summary>The stream ID. If the stream ID hasn't been assigned yet, an exception is thrown. Assigning the
        /// stream ID registers the stream with the multi-stream connection.</summary>
        /// <exception cref="InvalidOperationException">If the stream ID has not been assigned yet.</exception>
        long Id { get; }

        /// <summary>Returns <c>true</c> if the stream is a bidirectional stream, <c>false</c> otherwise.</summary>
        bool IsBidirectional { get; }

        /// <summary>Sets the action which is called when the stream is reset.</summary>
        Action? ShutdownAction { get; set; }

        /// <summary>The transport header sentinel. Transport implementations that need to add an additional
        /// header to transmit data over the stream can provide the header data here. This can improve
        /// performance by reducing the number of allocations since the protocol implementation  will allocate
        /// buffer space for both the transport header and the protocol header. If a header is returned here,
        /// the implementation of the SendAsync method should expect this header to be set at the start of the
        /// first buffer.</summary>
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

        /// <summary>Reads data from the stream until the given buffer is full.</summary>
        /// <param name="buffer">The buffer that holds the read data.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A value task that completes once the buffer is filled up with read data.</returns>
        /// TODO: XXX Remove this, this is used by the Ice2 protocol implementation to read the header. It should
        /// instead use something like BufferedReceiver to receive the type and frame size.
        async ValueTask ReceiveUntilFullAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            // Loop until we received enough data to fully fill the given buffer.
            int offset = 0;
            while (offset < buffer.Length)
            {
                int received = await ReadAsync(buffer[offset..], cancel).ConfigureAwait(false);
                if (received == 0)
                {
                    throw new InvalidDataException("unexpected end of stream");
                }
                offset += received;
            }
        }

        /// <summary>Writes data over the stream.</summary>
        /// <param name="buffers">The buffers containing the data to write.</param>
        /// <param name="endStream">True if no more data will be sent over this stream, False otherwise.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A value task that completes once the buffers are written.</returns>
        ValueTask WriteAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, bool endStream, CancellationToken cancel);

        /// <summary>Waits for the stream shutdown completion.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A value task that completes once the stream is shutdown.</returns>
        ValueTask ShutdownCompleted(CancellationToken cancel);
    }
}
