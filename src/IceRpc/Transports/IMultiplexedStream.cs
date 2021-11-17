// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
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

        /// <summary>The transport header sentinel. Transport implementations that need an additional header to transmit
        /// data over the stream can provide a sample header here. The caller of <see cref="WriteAsync"/> is responsible
        /// for prepending this header to the buffers provided to <see cref="WriteAsync"/>. The implementation of <see
        /// cref="WriteAsync"/> expects this header to be present at the start of the <see cref="WriteAsync"/>
        /// buffers.</summary>
        ReadOnlyMemory<byte> TransportHeader { get; }

        /// <summary>Aborts the stream read side.</summary>
        /// <param name="errorCode">The reason of the abort.</param>
        void AbortRead(byte errorCode);

        /// <summary>Aborts the stream write side.</summary>
        /// <param name="errorCode">The reason of the abort.</param>
        void AbortWrite(byte errorCode);

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
