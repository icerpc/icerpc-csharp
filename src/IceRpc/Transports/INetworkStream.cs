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

    /// <summary>A network stream enables byte data exchange over a network stream managed by a <see
    /// cref="IMultiStreamConnection"/>.</summary>
    public interface INetworkStream
    {
        /// <summary>The stream ID. If the stream ID hasn't been assigned yet, an exception is thrown. Assigning the
        /// stream ID registers the stream with the connection.</summary>
        /// <exception cref="InvalidOperationException">If the stream ID has not been assigned yet.</exception>
        long Id { get; }

        /// <summary>Returns <c>true</c> if the stream is a bidirectional stream, <c>false</c> otherwise.</summary>
        bool IsBidirectional { get; }

        /// <summary>Returns <c>true</c> if reads are completed, , <c>false</c> otherwise.</summary>
        bool ReadsCompleted { get; }

        /// <summary>Sets the action which is called when the stream is reset.</summary>
        Action? ShutdownAction { get; set; }

        /// <summary>The transport header sentinel. Transport implementations that need to add an additional header
        /// to transmit data over the stream can provide the header data here. This can improve performance by reducing
        /// the number of allocations as Ice will allocate buffer space for both the transport header and the Ice
        /// protocol header. If a header is returned here, the implementation of the SendAsync method should expect
        /// this header to be set at the start of the first buffer.</summary>
        ReadOnlyMemory<byte> TransportHeader { get; }

        /// <summary>Abort the stream.</summary>
        /// <param name="errorCode">The reason of the abort.</param>
        void Abort(StreamError errorCode)
        {
            AbortRead(errorCode);
            AbortWrite(errorCode);
        }

        /// <summary>Abort the stream read side.</summary>
        /// <param name="errorCode">The reason of the abort.</param>
        void AbortRead(StreamError errorCode);

        /// <summary>Abort the stream write side.</summary>
        /// <param name="errorCode">The reason of the abort.</param>
        void AbortWrite(StreamError errorCode);

        /// <summary>Gets a <see cref="System.IO.Stream"/> to allow using this stream using a <see
        /// cref="System.IO.Stream"/></summary>
        /// <returns>The <see cref="System.IO.Stream"/> object.</returns>
        System.IO.Stream AsByteStream();

        /// <summary>Enable flow control for receiving data from the peer over the stream. This is called
        /// after receiving a request or response frame to receive data for a stream parameter. Flow control
        /// isn't enabled for receiving the request or response frame whose size is limited with
        /// IncomingFrameSizeMax. The stream relies on the underlying transport flow control instead (TCP,
        /// Quic, ...). For stream parameters, whose size is not limited, it's important that the transport
        /// doesn't send an unlimited amount of data if the receiver doesn't process it. For TCP based
        /// transports, this would cause the send buffer to fill up and this would prevent other streams to be
        /// processed.</summary>
        void EnableReceiveFlowControl();

        /// <summary>Enable flow control for sending data to the peer over the stream. This is called after
        /// sending a request or response frame to send data from a stream parameter.</summary>
        void EnableSendFlowControl();

        /// <summary>Receives data from the stream.</summary>
        /// <param name="buffer">The buffer that holds the received data.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>The number of bytes received.</returns>
        ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken cancel);

        /// <summary>Receives data from the stream until the given buffer is full.</summary>
        /// <param name="buffer">The buffer that holds the received data.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A value task that completes once the buffer is filled up with received data.</returns>
        /// TODO: XXX REMOVE
        async ValueTask ReceiveUntilFullAsync(Memory<byte> buffer, CancellationToken cancel)
        {
            // Loop until we received enough data to fully fill the given buffer.
            int offset = 0;
            while (offset < buffer.Length)
            {
                int received = await ReceiveAsync(buffer[offset..], cancel).ConfigureAwait(false);
                if (received == 0)
                {
                    throw new InvalidDataException("unexpected end of stream");
                }
                offset += received;
            }
        }

        /// <summary>Sends data over the stream.</summary>
        /// <param name="buffers">The buffers containing the data to send.</param>
        /// <param name="endStream">True if no more data will be sent over this stream, False otherwise.</param>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A value task that completes once the buffers are sent.</returns>
        ValueTask SendAsync(ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, bool endStream, CancellationToken cancel);

        /// <summary>Wait for the stream shutdown completion.</summary>
        /// <param name="cancel">A cancellation token that receives the cancellation requests.</param>
        /// <returns>A value task that completes once the stream is shutdown.</returns>
        ValueTask ShutdownCompleted(CancellationToken cancel);
    }
}
